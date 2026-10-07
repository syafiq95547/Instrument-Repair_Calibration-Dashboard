using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using InstrumentHub.Api.Data;
using InstrumentHub.Api.Services;
using InstrumentHub.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var demoMode = config.GetValue<bool>("DemoMode");
if (demoMode && !builder.Environment.IsDevelopment()) throw new InvalidOperationException("DemoMode is allowed only in Development.");
var jwtKey = config["Jwt:Key"] ?? throw new InvalidOperationException("Configure Jwt__Key with a secret of at least 32 bytes.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt__Key must be at least 32 bytes.");
if (!builder.Environment.IsDevelopment() && (jwtKey.StartsWith("development-only") || string.IsNullOrWhiteSpace(config["Admin:PasswordHash"])))
    throw new InvalidOperationException("Production requires a private JWT key and Admin__PasswordHash.");
builder.Services.AddDbContext<InstrumentDbContext>(o => o.UseSqlServer(config.GetConnectionString("Instruments")));
builder.Services.AddSingleton<DemoData>();
builder.Services.AddScoped<AssetStore>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true,
        ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = config["Jwt:Issuer"],
        ValidAudience = config["Jwt:Audience"], IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ClockSkew = TimeSpan.FromSeconds(30) });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => {
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "InstrumentHub API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); app.UseWebAssemblyDebugging(); }
else { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
if (args.Contains("--initialize-database"))
{
    if (demoMode) throw new InvalidOperationException("Set DemoMode=false before database initialization.");
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Database.MigrateAsync();
    return;
}
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", mode = demoMode ? "demo" : "sql-server" }));
app.MapPost("/api/auth/login", (LoginRequest request) => {
    if(string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)) return Results.Unauthorized();
    var validEmail = string.Equals(request.Email, config["Admin:Email"], StringComparison.OrdinalIgnoreCase);
    var validPassword = config["Admin:PasswordHash"] is string hash ? VerifyPassword(request.Password, hash) :
        app.Environment.IsDevelopment() && FixedEquals(request.Password, config["Admin:Password"] ?? "");
    if (!validEmail || !validPassword) return Results.Unauthorized();
    var expires = DateTime.UtcNow.AddHours(8);
    var name = config["Admin:Name"] ?? "Administrator";
    var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
        [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Email, request.Email), new Claim(ClaimTypes.Role, "Administrator")],
        expires: expires, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256));
    return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), name, expires));
}).RequireRateLimiting("login");
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/instruments", async (AssetStore store) => (await store.Instruments()).OrderBy(x=>x.Id).Select(View));
api.MapGet("/dashboard", async (AssetStore store) => {
    var assets = (await store.Instruments()).Select(View).ToList();
    var eligible = assets.Count(x=>x.Status != "Out of Service");
    var compliant = assets.Count(x=>x.Status is "Calibrated" or "Due Soon");
    return new DashboardStats(assets.Count, assets.Count(x=>x.Status=="Overdue"), assets.Count(x=>x.Status=="Due Soon"),
        assets.Count(x=>x.Status=="In Repair"), assets.Count(x=>x.Status=="Calibrated"), assets.Count(x=>x.Status=="Pending Calibration"),
        assets.Count(x=>x.Status=="Out of Service"), eligible==0 ? 0 : Math.Round(100m*compliant/eligible,1),
        assets.Count==0 ? 0 : Math.Round(100m*compliant/assets.Count,1), (await store.Repairs()).Count(x=>x.Status!="Completed"), store.IsDemo);
});
api.MapPost("/instruments", async (Instrument asset, AssetStore store) => {
    asset.Id = 0;
    if (Validate(asset) is string error) return Results.BadRequest(new { message=error });
    try { await store.Save(asset); return Results.Created($"/api/instruments/{asset.Id}", View(asset)); }
    catch (InvalidOperationException e) { return Results.Conflict(new { message=e.Message }); }
});
api.MapPut("/instruments/{id:int}", async (int id, Instrument asset, AssetStore store) => {
    asset.Id = id;
    if (Validate(asset) is string error) return Results.BadRequest(new { message=error });
    try { return Results.Ok(View(await store.Save(asset))); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException e) { return Results.Conflict(new { message=e.Message }); }
});
api.MapGet("/repairs", async (AssetStore store) => (await store.Repairs()).OrderByDescending(x=>x.OpenedDate));
api.MapPost("/repairs", async (RepairLog log, AssetStore store) => {
    var asset = (await store.Instruments()).FirstOrDefault(x=>x.Id==log.InstrumentId);
    if(asset is null) return Results.NotFound();
    if(asset.CurrentStatus=="Out of Service") return Results.BadRequest(new { message="Restore the instrument to service before opening a repair." });
    if(string.IsNullOrWhiteSpace(log.Issue) || string.IsNullOrWhiteSpace(log.Technician) || log.Issue.Length>1000 || log.Technician.Length>100) return Results.BadRequest(new { message="Issue and technician are required (maximum 1000 and 100 characters)." });
    log.Id=0; log.AssetId=asset.AssetId; log.InstrumentName=asset.Name; log.Status="In Progress"; log.OpenedDate=DateOnly.FromDateTime(DateTime.UtcNow);
    try { await store.AddRepair(log); return Results.Ok(log); }
    catch(KeyNotFoundException) { return Results.NotFound(); }
    catch(InvalidOperationException e) { return Results.BadRequest(new { message=e.Message }); }
});
api.MapPost("/repairs/{id:int}/complete", async (int id, AssetStore store) => {
    try { await store.CompleteRepair(id); return Results.NoContent(); } catch(KeyNotFoundException) { return Results.NotFound(); }
});
api.MapGet("/certificates", async (AssetStore store) => (await store.Certificates()).OrderByDescending(x=>x.UploadedAt)
    .Select(x=>new { x.Id, x.InstrumentId, x.AssetId, x.FileName, x.UploadedAt, x.Size }));
api.MapPost("/instruments/{id:int}/certificates", async (int id, HttpRequest request, AssetStore store, IWebHostEnvironment env) => {
    var asset=(await store.Instruments()).FirstOrDefault(x=>x.Id==id);
    if(asset is null) return Results.NotFound();
    if(!request.HasFormContentType) return Results.BadRequest(new { message="Use multipart form data with a file field." });
    var file=(await request.ReadFormAsync()).Files.GetFile("file");
    if(file is null || file.Length<5 || file.Length>10*1024*1024) return Results.BadRequest(new { message="Choose a PDF certificate up to 10 MB." });
    var signature=new byte[5];
    await using(var input=file.OpenReadStream()) { await input.ReadExactlyAsync(signature); }
    if(!Encoding.ASCII.GetString(signature).Equals("%PDF-")) return Results.BadRequest(new { message="The certificate must be a valid PDF file." });
    var folder=Path.GetFullPath(config["Storage:Certificates"] ?? "App_Data/certificates", env.ContentRootPath);
    Directory.CreateDirectory(folder);
    var storageName=$"{Guid.NewGuid():N}.pdf";
    var path=Path.Combine(folder,storageName);
    await using(var output=File.Create(path)) { await file.CopyToAsync(output); }
    var cert=new Certificate { InstrumentId=id, AssetId=asset.AssetId, FileName=Path.GetFileName(file.FileName), StorageName=storageName, UploadedAt=DateTime.UtcNow, Size=file.Length };
    try { await store.AddCertificate(cert); } catch { File.Delete(path); throw; }
    return Results.Created($"/api/certificates/{cert.Id}/download", new { cert.Id, cert.FileName });
}).DisableAntiforgery();
api.MapGet("/certificates/{id:int}/download", async (int id, AssetStore store, IWebHostEnvironment env) => {
    var cert=(await store.Certificates()).FirstOrDefault(x=>x.Id==id);
    if(cert is null) return Results.NotFound();
    var folder=Path.GetFullPath(config["Storage:Certificates"] ?? "App_Data/certificates",env.ContentRootPath);
    var path=Path.Combine(folder,cert.StorageName);
    return File.Exists(path) ? Results.File(path,"application/pdf",cert.FileName) : Results.NotFound();
});
api.MapGet("/instruments/export", async (AssetStore store) => {
    using var writer = new StringWriter(CultureInfo.InvariantCulture);
    using(var csv = new CsvWriter(writer,CultureInfo.InvariantCulture)) { csv.WriteRecords((await store.Instruments()).Select(x=>new ImportRow {
        AssetId=SafeCell(x.AssetId), Name=SafeCell(x.Name), Model=SafeCell(x.Model), Department=SafeCell(x.Department), Location=SafeCell(x.Location), Owner=SafeCell(x.Owner),
        CurrentStatus=x.CurrentStatus, NextDueDate=x.NextDueDate?.ToString("yyyy-MM-dd") ?? "" })); }
    return Results.File(Encoding.UTF8.GetBytes(writer.ToString()),"text/csv",$"instruments-{DateTime.UtcNow:yyyy-MM-dd}.csv");
});
api.MapPost("/instruments/import", async (HttpRequest request, AssetStore store) => {
    if(!request.HasFormContentType) return Results.BadRequest(new { message="Upload a CSV or XLSX file." });
    var file=(await request.ReadFormAsync()).Files.GetFile("file");
    if(file is null || file.Length==0 || file.Length>5*1024*1024) return Results.BadRequest(new { message="Import files must be under 5 MB." });
    try {
        List<ImportRow> rows;
        var extension=Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream=file.OpenReadStream();
        if(extension==".csv") {
            using var reader=new StreamReader(stream);
            using var csv=new CsvReader(reader,new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated=null, MissingFieldFound=null });
            if(!csv.Read()) return Results.BadRequest(new { message="The CSV file is empty." });
            csv.ReadHeader();
            if(!new[]{"AssetId","Name","Department"}.All(h=>csv.HeaderRecord?.Contains(h)==true)) return Results.BadRequest(new { message="Required columns: AssetId, Name, Department." });
            rows=[]; while(rows.Count<501 && csv.Read()) rows.Add(csv.GetRecord<ImportRow>());
        }
        else if(extension==".xlsx") {
            using var workbook=new XLWorkbook(stream); var sheet=workbook.Worksheet(1); rows=[];
            var headers=sheet.Row(1).CellsUsed().ToDictionary(c=>c.GetString(),c=>c.Address.ColumnNumber,StringComparer.OrdinalIgnoreCase);
            if(!new[]{"AssetId","Name","Department"}.All(headers.ContainsKey)) return Results.BadRequest(new { message="Required columns: AssetId, Name, Department." });
            foreach(var row in sheet.RowsUsed().Skip(1).Take(501)) {
                string Get(string name)=>headers.TryGetValue(name,out var index) ? row.Cell(index).GetString() : "";
                var date=headers.TryGetValue("NextDueDate",out var dateColumn) && row.Cell(dateColumn).DataType==XLDataType.DateTime ? row.Cell(dateColumn).GetDateTime().ToString("yyyy-MM-dd") : Get("NextDueDate");
                rows.Add(new ImportRow { AssetId=Get("AssetId"),Name=Get("Name"),Department=Get("Department"),Model=Get("Model"),Location=Get("Location"),Owner=Get("Owner"),CurrentStatus=string.IsNullOrEmpty(Get("CurrentStatus")) ? "Active" : Get("CurrentStatus"),NextDueDate=date });
            }
        } else return Results.BadRequest(new { message="Supported formats: .csv and .xlsx." });
        if(rows.Count==0 || rows.Count>500) return Results.BadRequest(new { message="Import between 1 and 500 instruments at a time." });
        var assets=new List<Instrument>();
        foreach(var row in rows) {
            DateOnly? due=null;
            if(!string.IsNullOrWhiteSpace(row.NextDueDate)) { if(!DateOnly.TryParseExact(row.NextDueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed)) return Results.BadRequest(new { message=$"Invalid date for {row.AssetId}. Use yyyy-MM-dd." }); due=parsed; }
            var asset=new Instrument { AssetId=row.AssetId, Name=row.Name, Model=row.Model, Department=row.Department, Location=row.Location, Owner=row.Owner, CurrentStatus=row.CurrentStatus, NextDueDate=due };
            if(Validate(asset) is string error) return Results.BadRequest(new { message=error }); assets.Add(asset);
        }
        var existing=(await store.Instruments()).Select(x=>x.AssetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(assets.Any(x=>existing.Contains(x.AssetId)) || assets.Select(x=>x.AssetId).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=assets.Count) return Results.Conflict(new { message="Duplicate asset IDs. Import only new instruments." });
        await store.Import(assets);
        return Results.Ok(new ImportResult(assets.Count));
    } catch(InvalidOperationException e) { return Results.Conflict(new { message=e.Message }); }
    catch(Exception e) when(e is CsvHelperException or InvalidDataException or FormatException or ArgumentException) { return Results.BadRequest(new { message="The file could not be read. Check its columns and dates." }); }
}).DisableAntiforgery();
app.MapPost("/api/webhook/form-submit", async (FormSubmission submission, HttpRequest request, AssetStore store) => {
    var secret=config["Webhook:Key"];
    if(string.IsNullOrWhiteSpace(secret)) return Results.Problem("Webhook integration is not configured.",statusCode:503);
    if(!FixedEquals(request.Headers["X-Webhook-Key"].ToString(),secret)) return Results.Unauthorized();
    var asset=new Instrument { AssetId=submission.AssetId, Name=submission.InstrumentName, Department=submission.Department, NextDueDate=submission.NextDueDate, Owner=submission.Owner ?? "" };
    if(Validate(asset) is string error) return Results.BadRequest(new { message=error });
    try { await store.Save(asset); return Results.Created($"/api/instruments/{asset.Id}",View(asset)); } catch(InvalidOperationException e) { return Results.Conflict(new { message=e.Message }); }
}).RequireRateLimiting("login");
api.MapPost("/backups", async (InstrumentDbContext db, AssetStore store) => {
    if(store.IsDemo) return Results.BadRequest(new { message="Backups require SQL Server mode." });
    var target=config["Backup:UncPath"];
    if(string.IsNullOrWhiteSpace(target) || !target.StartsWith(@"\\") || target.Contains('\'')) return Results.BadRequest(new { message="Configure Backup__UncPath as a Windows UNC directory accessible by the SQL Server service account." });
    var database=db.Database.GetDbConnection().Database.Replace("]","]]");
    if(string.IsNullOrEmpty(database)) { await db.Database.OpenConnectionAsync(); database=db.Database.GetDbConnection().Database.Replace("]","]]"); }
    var path=target.TrimEnd('\\')+$"\\InstrumentHub-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
    // SQL Server, not the web process, writes this path. Only administrator-configured paths are accepted.
    await db.Database.OpenConnectionAsync();
    await using var command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = $"BACKUP DATABASE [{database}] TO DISK = @backupPath WITH COPY_ONLY, CHECKSUM";
    command.CommandTimeout = 600;
    var parameter = command.CreateParameter(); parameter.ParameterName = "@backupPath"; parameter.Value = path; command.Parameters.Add(parameter);
    await command.ExecuteNonQueryAsync();
    command.CommandText = "RESTORE VERIFYONLY FROM DISK = @backupPath WITH CHECKSUM";
    await command.ExecuteNonQueryAsync();
    return Results.Ok(new { message="SQL Server backup completed.", fileName=path.Split('\\').Last() });
}).RequireAuthorization(policy => policy.RequireRole("Administrator"));
app.MapFallbackToFile("index.html");
app.Run();

static InstrumentView View(Instrument x) => new(x.Id,x.AssetId,x.Name,x.Model,x.Department,x.Location,x.Owner,x.CurrentStatus,x.LastCalibrationDate,x.NextDueDate,CalibrationStatus.Calculate(x.CurrentStatus,x.NextDueDate,DateOnly.FromDateTime(DateTime.UtcNow)),x.TaskStatus);
static string? Validate(Instrument x) {
    x.AssetId=(x.AssetId ?? "").Trim(); x.Name=(x.Name ?? "").Trim(); x.Department=(x.Department ?? "").Trim();
    x.Model ??= ""; x.Location ??= ""; x.Owner ??= "";
    if(string.IsNullOrWhiteSpace(x.AssetId) || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Department)) return "Asset ID, instrument name, and department are required.";
    if(x.AssetId.Length>64 || x.Name.Length>160 || x.Department.Length>100 || x.Model.Length>160 || x.Location.Length>160 || x.Owner.Length>100) return "One or more fields exceed the allowed length.";
    if(!CalibrationStatus.ManualStatuses.Contains(x.CurrentStatus)) return "Stored status must be Active, In Repair, or Out of Service.";
    if(!CalibrationStatus.TaskStatuses.Contains(x.TaskStatus)) return "Invalid task status.";
    if(x.LastCalibrationDate is DateOnly last && x.NextDueDate is DateOnly next && next<last) return "Next due date cannot precede the last calibration date.";
    return null;
}
static bool FixedEquals(string a,string b) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)),SHA256.HashData(Encoding.UTF8.GetBytes(b)));
static bool VerifyPassword(string password,string encoded) {
    try { var parts=encoded.Split(':'); if(parts.Length!=3 || !int.TryParse(parts[0],out var iterations) || iterations<100000) return false;
        return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(parts[1]),iterations,HashAlgorithmName.SHA256,32),Convert.FromBase64String(parts[2]));
    } catch(FormatException) { return false; }
}
static string SafeCell(string value) => value.Length>0 && "=+-@\t\r".Contains(value[0]) ? "'"+value : value;
public sealed class ImportRow {
    public string AssetId { get; set; }=""; public string Name { get; set; }=""; public string Department { get; set; }="";
    public string Model { get; set; }=""; public string Location { get; set; }=""; public string Owner { get; set; }="";
    public string CurrentStatus { get; set; }="Active"; public string NextDueDate { get; set; }="";
}
