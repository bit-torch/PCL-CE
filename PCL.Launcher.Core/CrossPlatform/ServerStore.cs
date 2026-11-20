using fNbt;

namespace PCL.CrossPlatform;

public sealed record SavedServer(string Name, string Address)
{
    public override string ToString() => $"{Name} · {Address}";
}

public static class ServerStore
{
    public static IReadOnlyList<SavedServer> Read(string file)
    {
        if (!File.Exists(file)) return [];
        var nbt = new NbtFile(file);
        return (nbt.RootTag.Get<NbtList>("servers") ?? new NbtList("servers", NbtTagType.Compound))
            .Cast<NbtCompound>().Select(item => new SavedServer(item.Get<NbtString>("name")?.Value ?? "", item.Get<NbtString>("ip")?.Value ?? "")).ToArray();
    }
    public static void Write(string file, IReadOnlyList<SavedServer> servers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var nbt = File.Exists(file) ? new NbtFile(file) : new NbtFile(new NbtCompound(""));
        var existing = nbt.RootTag.Get<NbtList>("servers")?.Cast<NbtCompound>().ToList() ?? [];
        var list = new NbtList("servers", NbtTagType.Compound);
        foreach (var server in servers)
        {
            var entry = existing.FirstOrDefault(item => item.Get<NbtString>("ip")?.Value == server.Address)?.Clone() as NbtCompound ?? new NbtCompound();
            entry["name"] = new NbtString("name", server.Name); entry["ip"] = new NbtString("ip", server.Address);
            list.Add(entry);
        }
        nbt.RootTag["servers"] = list;
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            nbt.SaveToFile(temporary, NbtCompression.None);
            if (File.Exists(file)) File.Copy(file, file + ".bak", true);
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
