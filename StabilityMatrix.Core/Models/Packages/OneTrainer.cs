﻿using System.Diagnostics;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Helper.Cache;
using StabilityMatrix.Core.Helper.HardwareInfo;
using StabilityMatrix.Core.Models.Progress;
using StabilityMatrix.Core.Processes;
using StabilityMatrix.Core.Python;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Core.Models.Packages;

[Singleton(typeof(BasePackage))]
public class OneTrainer(
    IGithubApiCache githubApi,
    ISettingsManager settingsManager,
    IDownloadService downloadService,
    IPrerequisiteHelper prerequisiteHelper
) : BaseGitPackage(githubApi, settingsManager, downloadService, prerequisiteHelper)
{
    public override string Name => "OneTrainer";
    public override string DisplayName { get; set; } = "OneTrainer";
    public override string Author => "Nerogar";
    public override string Blurb =>
        "OneTrainer is a one-stop solution for all your stable diffusion training needs";
    public override string LicenseType => "AGPL-3.0";
    public override string LicenseUrl => "https://github.com/Nerogar/OneTrainer/blob/master/LICENSE.txt";
    public override string LaunchCommand => "scripts/train_ui.py";

    public override Uri PreviewImageUri =>
        new("https://github.com/Nerogar/OneTrainer/blob/master/resources/icons/icon.png?raw=true");

    public override string OutputFolderName => string.Empty;
    public override SharedFolderMethod RecommendedSharedFolderMethod => SharedFolderMethod.None;
    public override IEnumerable<TorchVersion> AvailableTorchVersions => [TorchVersion.Cuda];
    public override bool IsCompatible => HardwareHelper.HasNvidiaGpu();
    public override PackageType PackageType => PackageType.SdTraining;
    public override IEnumerable<SharedFolderMethod> AvailableSharedFolderMethods =>
        new[] { SharedFolderMethod.None };
    public override PackageDifficulty InstallerSortOrder => PackageDifficulty.Nightmare;
    public override bool OfferInOneClickInstaller => false;
    public override bool ShouldIgnoreReleases => true;
    public override IEnumerable<PackagePrerequisite> Prerequisites =>
        base.Prerequisites.Concat([PackagePrerequisite.Tkinter]);

    public override async Task InstallPackage(
        string installLocation,
        TorchVersion torchVersion,
        SharedFolderMethod selectedSharedFolderMethod,
        DownloadPackageVersionOptions versionOptions,
        IProgress<ProgressReport>? progress = null,
        Action<ProcessOutput>? onConsoleOutput = null
    )
    {
        progress?.Report(new ProgressReport(-1f, "Setting up venv", isIndeterminate: true));

        await using var venvRunner = await SetupVenvPure(installLocation).ConfigureAwait(false);

        progress?.Report(new ProgressReport(-1f, "Installing requirements", isIndeterminate: true));

        var pipArgs = new PipInstallArgs("-r", "requirements.txt");
        await venvRunner.PipInstall(pipArgs, onConsoleOutput).ConfigureAwait(false);
    }

    public override async Task RunPackage(
        string installedPackagePath,
        string command,
        string arguments,
        Action<ProcessOutput>? onConsoleOutput
    )
    {
        await SetupVenv(installedPackagePath).ConfigureAwait(false);
        var args = $"\"{Path.Combine(installedPackagePath, command)}\" {arguments}";

        VenvRunner?.RunDetached(args.TrimEnd(), HandleConsoleOutput, HandleExit);
        return;

        void HandleExit(int i)
        {
            Debug.WriteLine($"Venv process exited with code {i}");
            OnExit(i);
        }

        void HandleConsoleOutput(ProcessOutput s)
        {
            onConsoleOutput?.Invoke(s);
        }
    }

    public override List<LaunchOptionDefinition> LaunchOptions => [LaunchOptionDefinition.Extras];
    public override Dictionary<SharedFolderType, IReadOnlyList<string>>? SharedFolders { get; }
    public override Dictionary<SharedOutputType, IReadOnlyList<string>>? SharedOutputFolders { get; }
    public override string MainBranch => "master";
}
