using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using static System.Security.Cryptography.MD5;

namespace CM.RedisCache;

public static class RedisCache
{
    public static IConnectionMultiplexer Multiplexer { get; set; }

    /// <summary>
    /// Returns the result of the query; if possible from the cache, otherwise
    /// the query is materialized and the result cached before being returned.
    /// </summary>
    public static async Task<IEnumerable<T>> FromCacheAsync<T>(this IQueryable<T> query, TimeSpan expiry)
    {
        // Multiplexer not null check
        ArgumentNullException.ThrowIfNull(Multiplexer);

        // Get the query unique key
        var key = query.GetCacheKey();

        // Use the working redis database
        var db = Multiplexer.GetDatabase();

        var value = db.StringGet(key);

        // Try to get the query result from the cache
        if (value.IsNullOrEmpty)
        {
            // materialize the query
            var result = await query.ToListAsync();

            // Put result in cache and return
            db.StringSet(key, JsonSerializer.Serialize(result), expiry);
            return result;
        }

        return JsonSerializer.Deserialize<List<T>>((string)value);
    }

    /// <summary>
    /// Gets a cache key for a query.
    /// </summary>
    public static string GetCacheKey(this IQueryable query)
    {
        Debug.WriteLine(Multiplexer.ClientName);

        var expression = query.Expression;

        // locally evaluate as much of the query as possible
        expression = Evaluator.PartialEval(expression, CanBeEvaluatedLocally);

        // support local collections
        expression = LocalCollectionExpander.Rewrite(expression);

        // use the string representation of the expression for the cache key
        var key = expression.ToString();

        // the key is potentially very long, so use md5 fingerprint (fine if the query result data isn't critically sensitive)
        key = key.ToMd5();

        return key;
    }

    private static Func<Expression, bool> CanBeEvaluatedLocally
    {
        get
        {
            return expression =>
            {
                // don't evaluate parameters
                if (expression.NodeType == ExpressionType.Parameter)
                    return false;

                // can't evaluate queries
                if (typeof(IQueryable).IsAssignableFrom(expression.Type))
                    return false;

                return true;
            };
        }
    }
}

/// <summary>
/// Enables the partial evaluation of queries.
/// </summary>
public static class Evaluator
{
    /// <summary>
    /// Performs evaluation & replacement of independent sub-trees
    /// </summary>
    /// <param name="expression">The root of the expression tree.</param>
    /// <param name="fnCanBeEvaluated">A function that decides whether a given expression node can be part of the local function.</param>
    /// <returns>A new tree with sub-trees evaluated and replaced.</returns>
    public static Expression PartialEval(Expression expression, Func<Expression, bool> fnCanBeEvaluated)
    {
        return new SubtreeEvaluator(new Nominator(fnCanBeEvaluated).Nominate(expression)).Eval(expression);
    }

    /// <summary>
    /// Performs evaluation & replacement of independent sub-trees
    /// </summary>
    /// <param name="expression">The root of the expression tree.</param>
    /// <returns>A new tree with sub-trees evaluated and replaced.</returns>
    public static Expression PartialEval(Expression expression)
    {
        return PartialEval(expression, CanBeEvaluatedLocally);
    }

    private static bool CanBeEvaluatedLocally(Expression expression)
    {
        return expression.NodeType != ExpressionType.Parameter;
    }

    /// <summary>
    /// Performs bottom-up analysis to determine which nodes can possibly
    /// be part of an evaluated sub-tree.
    /// </summary>
    private class Nominator : ExpressionVisitor
    {
        private Func<Expression, bool> fnCanBeEvaluated;
        private HashSet<Expression> candidates;
        private bool cannotBeEvaluated;

        internal Nominator(Func<Expression, bool> fnCanBeEvaluated)
        {
            this.fnCanBeEvaluated = fnCanBeEvaluated;
        }

        internal HashSet<Expression> Nominate(Expression expression)
        {
            candidates = new HashSet<Expression>();
            Visit(expression);
            return candidates;
        }

        public override Expression Visit(Expression expression)
        {
            if (expression != null)
            {
                var saveCannotBeEvaluated = cannotBeEvaluated;
                cannotBeEvaluated = false;
                base.Visit(expression);
                if (!cannotBeEvaluated)
                {
                    if (fnCanBeEvaluated(expression))
                        candidates.Add(expression);
                    else
                        cannotBeEvaluated = true;
                }

                cannotBeEvaluated |= saveCannotBeEvaluated;
            }

            return expression;
        }
    }

    /// <summary>
    /// Evaluates & replaces sub-trees when first candidate is reached (top-down)
    /// </summary>
    private class SubtreeEvaluator : ExpressionVisitor
    {
        private HashSet<Expression> candidates;

        internal SubtreeEvaluator(HashSet<Expression> candidates)
        {
            this.candidates = candidates;
        }

        internal Expression Eval(Expression exp)
        {
            return Visit(exp);
        }

        public override Expression Visit(Expression exp)
        {
            if (exp == null) return null;

            if (candidates.Contains(exp)) return Evaluate(exp);

            return base.Visit(exp);
        }

        private Expression Evaluate(Expression e)
        {
            if (e.NodeType == ExpressionType.Constant) return e;

            var lambda = Expression.Lambda(e);
            var fn = lambda.Compile();
            return Expression.Constant(fn.DynamicInvoke(null), e.Type);
        }
    }
}

public static class Utility
{
    /// <summary>
    /// Creates an MD5 fingerprint of the string.
    /// </summary>
    public static string ToMd5(this string s)
    {
        var bytes = Encoding.Unicode.GetBytes(s.ToCharArray());
        var hash = HashData(bytes);

        // concat the hash bytes into one long string
        return hash.Aggregate(new StringBuilder(32),
                (sb, b) => sb.Append(b.ToString("X2")))
            .ToString();
    }

    public static string ToConcatenatedString<T>(this IEnumerable<T> source, Func<T, string> selector, string separator)
    {
        var b = new StringBuilder();
        var needSeparator = false;

        foreach (var item in source)
        {
            if (needSeparator)
                b.Append(separator);

            b.Append(selector(item));
            needSeparator = true;
        }

        return b.ToString();
    }

    public static LinkedList<T> ToLinkedList<T>(this IEnumerable<T> source)
    {
        return new LinkedList<T>(source);
    }
}

/// <summary>
/// Enables cache key support for local collection values.
/// </summary>
public class LocalCollectionExpander : ExpressionVisitor
{
    public static Expression Rewrite(Expression expression)
    {
        return new LocalCollectionExpander().Visit(expression);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // pair the method's parameter types with its arguments
        var map = node.Method.GetParameters()
            .Zip(node.Arguments, (p, a) => new { Param = p.ParameterType, Arg = a })
            .ToLinkedList();

        // deal with instance methods
        var instanceType = node.Object == null ? null : node.Object.Type;
        map.AddFirst(new { Param = instanceType, Arg = node.Object });

        // for any local collection parameters in the method, make a
        // replacement argument which will print its elements
        var replacements = (from x in map
            where x.Param != null && x.Param.IsGenericType
            let g = x.Param.GetGenericTypeDefinition()
            where g == typeof(IEnumerable<>) || g == typeof(List<>)
            where x.Arg.NodeType == ExpressionType.Constant
            let elementType = x.Param.GetGenericArguments().Single()
            let printer = MakePrinter((ConstantExpression)x.Arg, elementType)
            select new { x.Arg, Replacement = printer }).ToList();

        if (replacements.Any())
        {
            var args = map.Select(x => (from r in replacements
                where r.Arg == x.Arg
                select r.Replacement).SingleOrDefault() ?? x.Arg).ToList();

            node = node.Update(args.First(), args.Skip(1));
        }

        return base.VisitMethodCall(node);
    }

    private ConstantExpression MakePrinter(ConstantExpression enumerable, Type elementType)
    {
        var value = (IEnumerable)enumerable.Value;
        var printerType = typeof(Printer<>).MakeGenericType(elementType);
        var printer = Activator.CreateInstance(printerType, value);

        return Expression.Constant(printer);
    }

    /// <summary>
    /// Overrides ToString to print each element of a collection.
    /// </summary>
    /// <remarks>
    /// Inherits List in order to support List.Contains instance method as well
    /// as standard Enumerable.Contains/Any extension methods.
    /// </remarks>
    private class Printer<T> : List<T>
    {
        public Printer(IEnumerable collection)
        {
            AddRange(collection.Cast<T>());
        }

        public override string ToString()
        {
            return "{" + this.ToConcatenatedString(t => t.ToString(), "|") + "}";
        }
    }
}