using InstrumentHub.Api.Data;
using InstrumentHub.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
namespace InstrumentHub.Api.Services;
public sealed class AssetStore(InstrumentDbContext db, DemoData demo, IConfiguration configuration)
{
    public bool IsDemo => configuration.GetValue<bool>("DemoMode");
    public async Task<List<Instrument>> Instruments() => IsDemo ? await ReadDemo(()=>demo.Instruments.Select(Clone).ToList()) : await db.Instruments.AsNoTracking().ToListAsync();
    static Instrument Clone(Instrument x) => new() { Id=x.Id, AssetId=x.AssetId, Name=x.Name, Model=x.Model, Department=x.Department, Location=x.Location, Owner=x.Owner, CurrentStatus=x.CurrentStatus, LastCalibrationDate=x.LastCalibrationDate, NextDueDate=x.NextDueDate, TaskStatus=x.TaskStatus };
    public async Task<List<RepairLog>> Repairs() => IsDemo ? await ReadDemo(()=>demo.Repairs.Select(x=>new RepairLog { Id=x.Id, InstrumentId=x.InstrumentId, AssetId=x.AssetId, InstrumentName=x.InstrumentName, Issue=x.Issue, Technician=x.Technician, Status=x.Status, OpenedDate=x.OpenedDate }).ToList()) : await db.RepairLogs.AsNoTracking().ToListAsync();
    public async Task<List<Certificate>> Certificates() => IsDemo ? await ReadDemo(()=>demo.Certificates.Select(x=>new Certificate { Id=x.Id, InstrumentId=x.InstrumentId, AssetId=x.AssetId, FileName=x.FileName, StorageName=x.StorageName, ContentType=x.ContentType, UploadedAt=x.UploadedAt, Size=x.Size }).ToList()) : await db.Certificates.AsNoTracking().ToListAsync();
    async Task<List<T>> ReadDemo<T>(Func<List<T>> read) { await demo.Gate.WaitAsync(); try { return read(); } finally { demo.Gate.Release(); } }
    async Task Persist() {
        try { await db.SaveChangesAsync(); }
        catch(DbUpdateException e) when(e.InnerException is SqlException { Number: 2601 or 2627 }) { throw new InvalidOperationException("This asset ID already exists."); }
    }
    public async Task<Instrument> Save(Instrument asset)
    {
        if (IsDemo)
        {
            await demo.Gate.WaitAsync();
            try
            {
                if (demo.Instruments.Any(x => string.Equals(x.AssetId,asset.AssetId,StringComparison.OrdinalIgnoreCase) && x.Id != asset.Id)) throw new InvalidOperationException("This asset ID already exists.");
                if (asset.Id == 0) { asset.Id = demo.Instruments.Select(x => x.Id).DefaultIfEmpty().Max() + 1; demo.Instruments.Add(Clone(asset)); }
                else { var index = demo.Instruments.FindIndex(x => x.Id == asset.Id); if(index < 0) throw new KeyNotFoundException(); demo.Instruments[index] = Clone(asset); }
            }
            finally { demo.Gate.Release(); }
        }
        else
        {
            if (await db.Instruments.AnyAsync(x => x.AssetId == asset.AssetId && x.Id != asset.Id)) throw new InvalidOperationException("This asset ID already exists.");
            if(asset.Id == 0) db.Instruments.Add(asset);
            else { if(!await db.Instruments.AnyAsync(x=>x.Id==asset.Id)) throw new KeyNotFoundException(); db.Instruments.Update(asset); }
            await Persist();
        }
        return asset;
    }
    public async Task Import(List<Instrument> assets)
    {
        if(IsDemo) {
            await demo.Gate.WaitAsync();
            try {
                if(assets.Any(a=>demo.Instruments.Any(x=>string.Equals(x.AssetId,a.AssetId,StringComparison.OrdinalIgnoreCase)))) throw new InvalidOperationException("Duplicate asset IDs.");
                var next=demo.Instruments.Select(x=>x.Id).DefaultIfEmpty().Max()+1;
                foreach(var asset in assets) { asset.Id=next++; demo.Instruments.Add(Clone(asset)); }
            } finally { demo.Gate.Release(); }
        } else { db.Instruments.AddRange(assets); await Persist(); }
    }
    public async Task AddCertificate(Certificate cert)
    {
        if (IsDemo) { await demo.Gate.WaitAsync(); try { cert.Id = demo.Certificates.Select(x => x.Id).DefaultIfEmpty().Max() + 1; demo.Certificates.Add(cert); } finally { demo.Gate.Release(); } }
        else { db.Certificates.Add(cert); await Persist(); }
    }
    public async Task<RepairLog> AddRepair(RepairLog log)
    {
        async Task Apply()
        {
            var asset = IsDemo ? demo.Instruments.FirstOrDefault(x=>x.Id==log.InstrumentId) : await db.Instruments.FindAsync(log.InstrumentId);
            if(asset is null) throw new KeyNotFoundException();
            if(asset.CurrentStatus=="Out of Service") throw new InvalidOperationException("Restore the instrument to service before opening a repair.");
            log.AssetId=asset.AssetId; log.InstrumentName=asset.Name; asset.CurrentStatus="In Repair";
            if(IsDemo) { log.Id=demo.Repairs.Select(x=>x.Id).DefaultIfEmpty().Max()+1; demo.Repairs.Add(log); }
            else { db.RepairLogs.Add(log); await Persist(); }
        }
        if(IsDemo) { await demo.Gate.WaitAsync(); try { await Apply(); } finally { demo.Gate.Release(); } }
        else await Apply();
        return log;
    }
    public async Task CompleteRepair(int id)
    {
        async Task Apply()
        {
            var log = IsDemo ? demo.Repairs.FirstOrDefault(x=>x.Id==id) : await db.RepairLogs.FindAsync(id);
            if(log is null) throw new KeyNotFoundException();
            var asset = IsDemo ? demo.Instruments.First(x=>x.Id==log.InstrumentId) : await db.Instruments.FindAsync(log.InstrumentId);
            if(asset is null) throw new KeyNotFoundException();
            var hasOtherRepairs=IsDemo ? demo.Repairs.Any(x=>x.Id!=id && x.InstrumentId==asset.Id && x.Status!="Completed") : await db.RepairLogs.AnyAsync(x=>x.Id!=id && x.InstrumentId==asset.Id && x.Status!="Completed");
            log.Status="Completed";
            if(asset.CurrentStatus=="In Repair" && !hasOtherRepairs) asset.CurrentStatus="Active";
            if(!IsDemo) await Persist();
        }
        if(IsDemo) { await demo.Gate.WaitAsync(); try { await Apply(); } finally { demo.Gate.Release(); } }
        else await Apply();
    }
}
