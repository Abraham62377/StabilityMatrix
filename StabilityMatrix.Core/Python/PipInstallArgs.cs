﻿using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Processes;

namespace StabilityMatrix.Core.Python;

public record PipInstallArgs : ProcessArgsBuilder
{
    public PipInstallArgs(params Argument[] arguments)
        : base(arguments) { }

    public PipInstallArgs WithTorch(string version = "") => this.AddArg($"torch{version}");

    public PipInstallArgs WithTorchDirectML(string version = "") => this.AddArg($"torch-directml{version}");

    public PipInstallArgs WithTorchVision(string version = "") => this.AddArg($"torchvision{version}");

    public PipInstallArgs WithXFormers(string version = "") => this.AddArg($"xformers{version}");

    public PipInstallArgs WithExtraIndex(string indexUrl) => this.AddArg(("--extra-index-url", indexUrl));

    public PipInstallArgs WithTorchExtraIndex(string index) =>
        this.AddArg(("--extra-index-url", $"https://download.pytorch.org/whl/{index}"));

    public PipInstallArgs WithParsedFromRequirementsTxt(
        string requirements,
        [StringSyntax(StringSyntaxAttribute.Regex)] string? excludePattern = null
    )
    {
        var requirementsEntries = requirements
            .SplitLines(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('#'))
            .Select(s => s.Contains('#') ? s.Substring(0, s.IndexOf('#')) : s);

        if (excludePattern is not null)
        {
            var excludeRegex = new Regex($"^{excludePattern}$");

            requirementsEntries = requirementsEntries.Where(s => !excludeRegex.IsMatch(s));
        }

        return this.AddArgs(requirementsEntries.Select(s => (Argument)s).ToArray());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return base.ToString();
    }
}
