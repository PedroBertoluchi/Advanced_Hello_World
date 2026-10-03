using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

if (args.Length == 1 && args[0] == "--self-test")
{
    var cases = new (string Name, string Before, string After, bool Expected)[]
    {
        ("whitespace", "class C { int X = 1; }", "class C\n{\n int X=1;\n}\n", true),
        ("literal", "class C { int X = 1; }", "class C { int X = 2; }", false),
        ("identifier", "class C { int X = 1; }", "class C { int Y = 1; }", false),
        ("string-space", "class C { string X = \"a b\"; }", "class C { string X = \"ab\"; }", false),
        ("directive", "#if A\nclass C {}\n#endif", "#if B\nclass C {}\n#endif", false),
        ("disabled-code", "#if A\nclass C {int X=1;}\n#endif", "#if A\nclass C {int X=2;}\n#endif", false),
        ("comment", "class C {} // one", "class C {} // two", false),
        ("syntax", "class C {}", "class C {", false)
    };
    foreach (var item in cases)
    {
        if (Equivalent(item.Before, item.After) != item.Expected)
            throw new InvalidOperationException($"Self-test failed: {item.Name}");
        Console.WriteLine($"PASS {item.Name}");
    }
    return 0;
}
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: FormatGuard <base-ref> <head-ref> | --self-test");
    return 2;
}
var entries = Git("diff", "--name-status", "--no-renames", args[0], args[1])
    .Split('\n', StringSplitOptions.RemoveEmptyEntries);
var checkedFiles = 0;
var failures = 0;
foreach (var entry in entries)
{
    var fields = entry.TrimEnd('\r').Split('\t');
    var path = fields[^1];
    if (path is ".github/workflows/format-guard.yml" or ".github/FormatGuard/FormatGuard.csproj" or ".github/FormatGuard/Program.cs" or ".github/FormatGuard/.gitignore")
        continue;
    if (fields[0] != "M" || !path.EndsWith(".cs", StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"Unsupported change: {path}");
        failures++;
        continue;
    }
    var before = Git("show", $"{args[0]}:{path}");
    var after = Git("show", $"{args[1]}:{path}");
    checkedFiles++;
    if (!Equivalent(before, after))
    {
        Console.Error.WriteLine($"Token, trivia or syntax mismatch: {path}");
        failures++;
    }
}
if (checkedFiles == 0)
{
    Console.Error.WriteLine("No modified C# files were checked.");
    return 1;
}
Console.WriteLine($"Checked {checkedFiles} C# files; failures {failures}.");
return failures == 0 ? 0 : 1;

static bool Equivalent(string before, string after)
{
    var left = CSharpSyntaxTree.ParseText(before);
    var right = CSharpSyntaxTree.ParseText(after);
    if (left.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error) ||
        right.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        return false;
    var a = left.GetRoot();
    var b = right.GetRoot();
    var tokensA = a.DescendantTokens(descendIntoTrivia: true).Select(t => (t.RawKind, t.Text, t.IsMissing));
    var tokensB = b.DescendantTokens(descendIntoTrivia: true).Select(t => (t.RawKind, t.Text, t.IsMissing));
    var triviaA = SignificantTrivia(a);
    var triviaB = SignificantTrivia(b);
    return tokensA.SequenceEqual(tokensB) && triviaA.SequenceEqual(triviaB);
}
static IEnumerable<(int, string)> SignificantTrivia(SyntaxNode root) =>
    root.DescendantTrivia(descendIntoTrivia: true)
        .Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
        .Select(t => (t.RawKind, t.ToFullString()));
static string Git(params string[] arguments)
{
    var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"git failed (exit {process.ExitCode}).");
    return output;
}
