using System.Diagnostics;
using System.Reflection;
using Silueta.Core;

if (args is ["--hold", string lockPath])
{
    using var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Console.WriteLine("ready");
    Console.ReadLine();
    return 0;
}
if (args is ["--save", string childVaultPath, string childSubject, string childName])
{
    PseudonymVault vault = PseudonymVault.LoadOrCreate(childVaultPath).Assign(childSubject, childName);
    Console.WriteLine("ready");
    Console.ReadLine();
    try
    {
        vault.SaveTo(childVaultPath);
        Console.WriteLine("saved");
        return 0;
    }
    catch (VaultWriteConflictException)
    {
        Console.WriteLine("conflict");
        return 5;
    }
}
if (args.Length != 0)
{
    Console.Error.WriteLine("Run without arguments to check persistence across processes.");
    return 2;
}

string directory = Directory.CreateTempSubdirectory("silueta-persistence-probe-").FullName;
var children = new List<Process>();
try
{
    string path = Path.Combine(directory, "vault.json");
    var vault = new PseudonymVault().Assign("s-1", "Ale Bravo");
    vault.SaveTo(path);
    string committed = File.ReadAllText(path);

    Process holder = Start("--hold", path + ".lock");
    await Ready(holder);
    try
    {
        vault.SaveTo(path);
        throw new InvalidOperationException("A writer ignored another process's lock.");
    }
    catch (VaultWriteConflictException) { }
    Require(File.ReadAllText(path) == committed, "A locked vault changed.");
    holder.Kill(entireProcessTree: true);
    await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    vault.SaveTo(path); // process death releases the handle; the empty sidecar can be reused

    Process first = Start("--save", path, "s-2", "Noa Toledo");
    Process second = Start("--save", path, "s-3", "Sol Castro");
    // Both children load the same state before either is allowed to save. No timing-based race.
    await Task.WhenAll(Ready(first), Ready(second));
    first.StandardInput.WriteLine("save");
    second.StandardInput.WriteLine("save");
    await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync())
        .WaitAsync(TimeSpan.FromSeconds(15));
    int[] codes = [first.ExitCode, second.ExitCode];
    Require(codes.Order().SequenceEqual([0, 5]), "Concurrent writers did not report one commit and one conflict.");
    PseudonymVault loaded = PseudonymVault.LoadOrCreate(path);
    Require(loaded.Count == 2 && loaded.SurrogateFor("s-1") == "Ale Bravo", "Concurrent save lost assignments.");
    string winner = first.ExitCode == 0 ? "s-2" : "s-3";
    string loser = first.ExitCode == 0 ? "s-3" : "s-2";
    Require(loaded.TryGetSurrogate(winner, out _), "The successful writer's subject is missing.");
    Require(!loaded.TryGetSurrogate(loser, out _), "The conflicting writer unexpectedly committed.");
    loaded.Assign(loser, loser == "s-2" ? "Noa Toledo" : "Sol Castro").SaveTo(path);
    Require(PseudonymVault.LoadOrCreate(path).Count == 3, "Reloading and repeating did not preserve both updates.");
    Require(Directory.GetFiles(directory, "*.tmp").Length == 0, "A temporary vault was left behind.");
    Console.WriteLine("Cross-process lock, process-death recovery, stale-save refusal and reload/retry passed.");
    return 0;
}
finally
{
    foreach (Process process in children)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process.Dispose();
    }
    Directory.Delete(directory, recursive: true);
}

Process Start(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    Process process = Process.Start(start)!;
    children.Add(process);
    return process;
}

static async Task Ready(Process process)
{
    string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
    Require(line == "ready", "The child process failed to reach its barrier.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
