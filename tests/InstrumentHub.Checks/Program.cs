using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using InstrumentHub.Shared;
int passed=0;
void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); passed++; }
var today=new DateOnly(2026,10,7);
(string? Stored,DateOnly? Due,string Expected)[] cases=[
("In Repair",null,"In Repair"),("In Repair",today.AddDays(-10),"In Repair"),("Out of Service",today.AddDays(300),"Out of Service"),("Out of Service",null,"Out of Service"),
("Active",null,"Pending Calibration"),(null,null,"Pending Calibration"),("Calibrated",today.AddDays(-1),"Overdue"),("Active",today,"Due Soon"),
("Active",today.AddDays(1),"Due Soon"),("Active",today.AddDays(30),"Due Soon"),("Active",today.AddDays(31),"Calibrated"),
("Overdue",today.AddDays(365),"Calibrated"),("In Progress",today.AddDays(-1),"Overdue"),("Out of Spec",today.AddDays(31),"Calibrated")];
foreach(var c in cases) Check(CalibrationStatus.Calculate(c.Stored,c.Due,today)==c.Expected,$"status {c.Stored ?? "null"}, {c.Due?.ToString() ?? "no date"} → {c.Expected}");
Check(CalibrationStatus.Calculate("Active",new DateOnly(2028,3,1),new DateOnly(2028,2,29))=="Due Soon","leap-day boundary");
if(args.Length==0){Console.WriteLine($"{passed} status checks passed. Pass the API base URL to run functional checks.");return;}
using var http=new HttpClient { BaseAddress=new Uri(args[0].TrimEnd('/')+"/") };
Check((await http.GetAsync("api/instruments")).StatusCode==HttpStatusCode.Unauthorized,"API requires JWT");
Check((await http.PostAsJsonAsync("api/auth/login",new LoginRequest("admin@instrumenthub.local","wrong"))).StatusCode==HttpStatusCode.Unauthorized,"invalid login rejected");
var loginResponse=await http.PostAsJsonAsync("api/auth/login",new LoginRequest("admin@instrumenthub.local","DemoCalibration!2026"));loginResponse.EnsureSuccessStatusCode();
var login=(await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!;http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.Token);
var initial=(await http.GetFromJsonAsync<List<InstrumentView>>("api/instruments"))!;
var summary=(await http.GetFromJsonAsync<DashboardStats>("api/dashboard"))!;
Check(initial.Count==summary.Total,"dashboard total matches inventory");
Check(initial.All(x=>x.Status==CalibrationStatus.Calculate(x.CurrentStatus,x.NextDueDate,DateOnly.FromDateTime(DateTime.UtcNow))),"API health follows status rules");
var suffix=Guid.NewGuid().ToString("N")[..8];var asset=new Instrument { AssetId="TEST-"+suffix,Name="Test pressure gauge",Department="Validation",NextDueDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(31) };
var created=await http.PostAsJsonAsync("api/instruments",asset);Check(created.StatusCode==HttpStatusCode.Created,"create instrument");
var view=(await created.Content.ReadFromJsonAsync<InstrumentView>())!;asset.Id=view.Id;Check(view.Status=="Calibrated","new instrument calculated health");
Check((await http.PostAsJsonAsync("api/instruments",asset)).StatusCode==HttpStatusCode.Conflict,"duplicate asset ID rejected");
asset.CurrentStatus="Overdue";Check((await http.PutAsJsonAsync($"api/instruments/{asset.Id}",asset)).StatusCode==HttpStatusCode.BadRequest,"derived status cannot be stored as manual override");
asset.CurrentStatus="Active";asset.NextDueDate=null;
var pending=await http.PutAsJsonAsync($"api/instruments/{asset.Id}",asset);pending.EnsureSuccessStatusCode();Check((await pending.Content.ReadFromJsonAsync<InstrumentView>())!.Status=="Pending Calibration","null due date recalculates pending");
var repair=await http.PostAsJsonAsync("api/repairs",new RepairLog { InstrumentId=asset.Id,Issue="Validation repair",Technician="Test technician" });repair.EnsureSuccessStatusCode();var log=(await repair.Content.ReadFromJsonAsync<RepairLog>())!;
Check((await http.GetFromJsonAsync<List<InstrumentView>>("api/instruments"))!.First(x=>x.Id==asset.Id).Status=="In Repair","repair applies manual override");
var complete=await http.PostAsync($"api/repairs/{log.Id}/complete",null);Check(complete.StatusCode==HttpStatusCode.NoContent,"complete repair");
Check((await http.GetFromJsonAsync<List<InstrumentView>>("api/instruments"))!.First(x=>x.Id==asset.Id).Status=="Pending Calibration","repair completion recalculates health");
var repairA=await http.PostAsJsonAsync("api/repairs",new RepairLog { InstrumentId=asset.Id,Issue="First parallel work order",Technician="Tester" });repairA.EnsureSuccessStatusCode();
var logA=(await repairA.Content.ReadFromJsonAsync<RepairLog>())!;
var repairB=await http.PostAsJsonAsync("api/repairs",new RepairLog { InstrumentId=asset.Id,Issue="Second parallel work order",Technician="Tester" });repairB.EnsureSuccessStatusCode();
var logB=(await repairB.Content.ReadFromJsonAsync<RepairLog>())!;
(await http.PostAsync($"api/repairs/{logA.Id}/complete",null)).EnsureSuccessStatusCode();
Check((await http.GetFromJsonAsync<List<InstrumentView>>("api/instruments"))!.First(x=>x.Id==asset.Id).Status=="In Repair","remaining open work order preserves In Repair");
asset.CurrentStatus="Out of Service";(await http.PutAsJsonAsync($"api/instruments/{asset.Id}",asset)).EnsureSuccessStatusCode();
(await http.PostAsync($"api/repairs/{logB.Id}/complete",null)).EnsureSuccessStatusCode();
Check((await http.GetFromJsonAsync<List<InstrumentView>>("api/instruments"))!.First(x=>x.Id==asset.Id).Status=="Out of Service","repair completion preserves Out of Service override");
async Task<HttpResponseMessage> Upload(string path,string name,byte[] bytes){using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes),"file",name);return await http.PostAsync(path,form);}
Check((await Upload($"api/instruments/{asset.Id}/certificates","bad.pdf",Encoding.UTF8.GetBytes("not a pdf"))).StatusCode==HttpStatusCode.BadRequest,"non-PDF certificate rejected");
Check((await Upload($"api/instruments/{asset.Id}/certificates","tiny.pdf",[1,2])).StatusCode==HttpStatusCode.BadRequest,"short PDF rejected without server error");
var pdf=Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
var uploaded=await Upload($"api/instruments/{asset.Id}/certificates","check.pdf",pdf);Check(uploaded.StatusCode==HttpStatusCode.Created,"upload PDF certificate");
var cert=await uploaded.Content.ReadFromJsonAsync<JsonElement>();var download=await http.GetAsync($"api/certificates/{cert.GetProperty("id").GetInt32()}/download");
Check((await download.Content.ReadAsByteArrayAsync()).SequenceEqual(pdf),"certificate download preserves bytes");
var csv=$"AssetId,Name,Department,Model,Location,Owner,CurrentStatus,NextDueDate\nCSV-{suffix},Imported gauge,Validation,,,Tester,Active,2027-03-01\n";
var imported=await Upload("api/instruments/import","assets.csv",Encoding.UTF8.GetBytes(csv));Check(imported.StatusCode==HttpStatusCode.OK,"CSV import");
Check((await Upload("api/instruments/import","assets.csv",Encoding.UTF8.GetBytes(csv))).StatusCode==HttpStatusCode.Conflict,"duplicate import rejected");
using var workbook=new XLWorkbook();var sheet=workbook.AddWorksheet("Assets");sheet.Cell(1,1).Value="AssetId";sheet.Cell(1,2).Value="Name";sheet.Cell(1,3).Value="Department";sheet.Cell(1,4).Value="NextDueDate";
sheet.Cell(2,1).Value="XLSX-"+suffix;sheet.Cell(2,2).Value="Imported balance";sheet.Cell(2,3).Value="Validation";sheet.Cell(2,4).Value=new DateTime(2027,4,1);
using var excel=new MemoryStream();workbook.SaveAs(excel);
Check((await Upload("api/instruments/import","assets.xlsx",excel.ToArray())).StatusCode==HttpStatusCode.OK,"Excel import with typed dates");
var exported=await http.GetStringAsync("api/instruments/export");Check(exported.Contains("CSV-"+suffix) && exported.Contains("XLSX-"+suffix),"CSV export includes imported records");
var beforeInvalid=(await http.GetFromJsonAsync<List<InstrumentView>>("api/instruments"))!.Count;
var invalidCsv=$"AssetId,Name,Department,Model,Location,Owner,CurrentStatus,NextDueDate\nATOMIC-{suffix},Valid,Validation,,,,Active,2027-01-01\nINVALID-{suffix},Invalid,Validation,,,,Active,not-a-date\n";
Check((await Upload("api/instruments/import","invalid.csv",Encoding.UTF8.GetBytes(invalidCsv))).StatusCode==HttpStatusCode.BadRequest,"invalid import rejected");
Check((await http.GetFromJsonAsync<List<InstrumentView>>("api/instruments"))!.Count==beforeInvalid,"invalid import does not partially create records");
var webhookKey=Environment.GetEnvironmentVariable("TEST_WEBHOOK_KEY");
if(webhookKey is not null){
var form=new FormSubmission("FORM-"+suffix,"Form gauge","Validation",null,"Test owner");
Check((await http.PostAsJsonAsync("api/webhook/form-submit",form)).StatusCode==HttpStatusCode.Unauthorized,"webhook rejects missing key");
using var request=new HttpRequestMessage(HttpMethod.Post,"api/webhook/form-submit"){Content=JsonContent.Create(form)};request.Headers.Add("X-Webhook-Key",webhookKey);
Check((await http.SendAsync(request)).StatusCode==HttpStatusCode.Created,"authenticated Google Form webhook creates instrument");
} else Console.WriteLine("SKIP: configured webhook integration (TEST_WEBHOOK_KEY not supplied)");
Check((await http.PostAsync("api/backups",null)).StatusCode==HttpStatusCode.BadRequest,"demo refuses SQL Server backup");
Check((await http.GetStringAsync("/")).Contains("blazor.webassembly.js"),"API serves Blazor client");
Console.WriteLine($"{passed} checks passed.");
