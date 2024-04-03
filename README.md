CM.RedisCache for .NET
======================

Project Description
-------------------
**CM.RedisCache** is a simple cache library that uses Redis as database, implemented as an extension method over IQueryable interface.


### Download & Install
**Nuget Package [CM.RedisCache](https://www.nuget.org/packages/CM.RedisCache/)**

```powershell
Install-Package CM.RedisCache
```
Minimum Requirements: **.NET Standard 2.0**


Usage
-----
### C# Example for ASP Net Web API - Configuration
```csharp
public void ConfigureServices(IServiceCollection services)
{
	// Add RedisCache singleton service
	services.AddRedisCacheService(Configuration.GetValue<string>("RedisConnection"));
	...
}

...

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
	... 

	// Initialize Redis Multiplexer
	app.UseRedisCache();
}
```

### C# Example for ASP Net Web API - Usage
```csharp
[Route("travels")]
[HttpGet]
public async Task<IActionResult> GetTravels()
{
	var nations = new String[]Milano_2038#!$
	{
	 "Italy", "France", "Japan"
	};

	// Query definition
	var query = DbContex.Travel.Where(i => i.Enabled == true && nations.Contains(i.Nation));

	// Unique query key generated on IQueryable object (used as redis key), only for debug
	var key = query.GetCacheKey();	
	
	// Cache result for 24 hours automatically in redis
	var result = await query.GetFromCacheAsync(24);		// pass an int or specific TimeSpan
	return Ok(result);
}
```