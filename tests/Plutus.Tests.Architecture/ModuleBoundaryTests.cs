using System.Text.RegularExpressions;
using Xunit;

namespace Plutus.Tests.Architecture;

/// <summary>
/// Enforces the modular-monolith boundary (architecture §6.1, T0.2/T0.4): a module never
/// references another module. Shared foundations (SharedKernel, Web.Infrastructure) and the
/// legacy shared libraries under Plutus/Commons are allowed. Works by parsing csproj files
/// on disk, so it stays decoupled from every module.
/// </summary>
public class ModuleBoundaryTests
{
    // The POS modules — each owns a slice and must not reference another module.
    private static readonly string[] Modules =
    {
        "Plutus.Identity", "Plutus.Tenancy", "Plutus.Sales",
        "Plutus.Catalogue", "Plutus.Reporting",
    };

    // Referenceable shared foundations (not modules).
    private static readonly string[] Shared = { "Plutus.SharedKernel", "Plutus.Web.Infrastructure" };

    [Fact]
    public void No_module_references_another_module()
    {
        var srcDir = Path.Combine(Repo.Root(), "src");
        var violations = new List<string>();

        foreach (var module in Modules)
        {
            var csproj = Path.Combine(srcDir, module, $"{module}.csproj");
            Assert.True(File.Exists(csproj), $"Missing module project: {csproj}");

            var refs = Regex.Matches(File.ReadAllText(csproj), @"ProjectReference\s+Include=""([^""]+)""")
                .Select(m => Path.GetFileNameWithoutExtension(m.Groups[1].Value));

            foreach (var referenced in refs)
            {
                var isOtherModule = Modules.Contains(referenced) && referenced != module;
                if (isOtherModule)
                    violations.Add($"{module} references module {referenced}");
                // Shared foundations and Plutus.Commons/* infra are allowed; anything else
                // that is a module is a violation (caught above).
                _ = Shared; // documents intent; shared refs are always permitted
            }
        }

        Assert.True(violations.Count == 0,
            "Module boundary violations:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void All_expected_modules_exist()
    {
        var srcDir = Path.Combine(Repo.Root(), "src");
        foreach (var module in Modules)
            Assert.True(File.Exists(Path.Combine(srcDir, module, $"{module}.csproj")), $"{module} project missing");
    }
}
