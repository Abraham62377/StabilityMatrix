﻿using System.Diagnostics;
using System.Text.RegularExpressions;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Helper.Cache;
using StabilityMatrix.Core.Helper.HardwareInfo;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Progress;
using StabilityMatrix.Core.Processes;
using StabilityMatrix.Core.Python;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Core.Models.Packages;

[Singleton(typeof(BasePackage))]
public class Fooocus(
    IGithubApiCache githubApi,
    ISettingsManager settingsManager,
    IDownloadService downloadService,
    IPrerequisiteHelper prerequisiteHelper
)
    : BaseGitPackage(githubApi, settingsManager, downloadService, prerequisiteHelper),
        ISharedFolderLayoutPackage
{
    public override string Name => "Fooocus";
    public override string DisplayName { get; set; } = "Fooocus";
    public override string Author => "lllyasviel";

    public override string Blurb => "Fooocus is a rethinking of Stable Diffusion and Midjourney’s designs";

    public override string LicenseType => "GPL-3.0";
    public override string LicenseUrl => "https://github.com/lllyasviel/Fooocus/blob/main/LICENSE";
    public override string LaunchCommand => "launch.py";

    public override Uri PreviewImageUri =>
        new(
            "https://user-images.githubusercontent.com/19834515/261830306-f79c5981-cf80-4ee3-b06b-3fef3f8bfbc7.png"
        );

    public override List<LaunchOptionDefinition> LaunchOptions =>
        new()
        {
            new LaunchOptionDefinition
            {
                Name = "Preset",
                Type = LaunchOptionType.Bool,
                Options = { "--preset anime", "--preset realistic" }
            },
            new LaunchOptionDefinition
            {
                Name = "Port",
                Type = LaunchOptionType.String,
                Description = "Sets the listen port",
                Options = { "--port" }
            },
            new LaunchOptionDefinition
            {
                Name = "Share",
                Type = LaunchOptionType.Bool,
                Description = "Set whether to share on Gradio",
                Options = { "--share" }
            },
            new LaunchOptionDefinition
            {
                Name = "Listen",
                Type = LaunchOptionType.String,
                Description = "Set the listen interface",
                Options = { "--listen" }
            },
            new LaunchOptionDefinition
            {
                Name = "Output Directory",
                Type = LaunchOptionType.String,
                Description = "Override the output directory",
                Options = { "--output-path" }
            },
            new LaunchOptionDefinition
            {
                Name = "Language",
                Type = LaunchOptionType.String,
                Description = "Change the language of the UI",
                Options = { "--language" }
            },
            new LaunchOptionDefinition
            {
                Name = "Auto-Launch",
                Type = LaunchOptionType.Bool,
                Options = { "--auto-launch" }
            },
            new LaunchOptionDefinition
            {
                Name = "Disable Image Log",
                Type = LaunchOptionType.Bool,
                Options = { "--disable-image-log" }
            },
            new LaunchOptionDefinition
            {
                Name = "Disable Analytics",
                Type = LaunchOptionType.Bool,
                Options = { "--disable-analytics" }
            },
            new LaunchOptionDefinition
            {
                Name = "Disable Preset Model Downloads",
                Type = LaunchOptionType.Bool,
                Options = { "--disable-preset-download" }
            },
            new LaunchOptionDefinition
            {
                Name = "Always Download Newer Models",
                Type = LaunchOptionType.Bool,
                Options = { "--always-download-new-model" }
            },
            new()
            {
                Name = "VRAM",
                Type = LaunchOptionType.Bool,
                InitialValue = HardwareHelper.IterGpuInfo().Select(gpu => gpu.MemoryLevel).Max() switch
                {
                    MemoryLevel.Low => "--always-low-vram",
                    MemoryLevel.Medium => "--always-normal-vram",
                    _ => null
                },
                Options =
                {
                    "--always-high-vram",
                    "--always-normal-vram",
                    "--always-low-vram",
                    "--always-no-vram"
                }
            },
            new LaunchOptionDefinition
            {
                Name = "Use DirectML",
                Type = LaunchOptionType.Bool,
                Description = "Use pytorch with DirectML support",
                InitialValue = HardwareHelper.PreferDirectML(),
                Options = { "--directml" }
            },
            new LaunchOptionDefinition
            {
                Name = "Disable Xformers",
                Type = LaunchOptionType.Bool,
                InitialValue = !HardwareHelper.HasNvidiaGpu(),
                Options = { "--disable-xformers" }
            },
            LaunchOptionDefinition.Extras
        };

    public override SharedFolderMethod RecommendedSharedFolderMethod => SharedFolderMethod.Configuration;

    public override IEnumerable<SharedFolderMethod> AvailableSharedFolderMethods =>
        new[] { SharedFolderMethod.Symlink, SharedFolderMethod.Configuration, SharedFolderMethod.None };

    public override Dictionary<SharedFolderType, IReadOnlyList<string>> SharedFolders =>
        ((ISharedFolderLayoutPackage)this).LegacySharedFolders;

    public virtual SharedFolderLayout SharedFolderLayout =>
        new()
        {
            RelativeConfigPath = "config.txt",
            Rules =
            [
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.StableDiffusion],
                    TargetRelativePaths = ["models/checkpoints"],
                    ConfigDocumentPaths = ["path_checkpoints"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.Diffusers],
                    TargetRelativePaths = ["models/diffusers"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.CLIP],
                    TargetRelativePaths = ["models/clip"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.GLIGEN],
                    TargetRelativePaths = ["models/gligen"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.ESRGAN],
                    TargetRelativePaths = ["models/upscale_models"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.Hypernetwork],
                    TargetRelativePaths = ["models/hypernetworks"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.TextualInversion],
                    TargetRelativePaths = ["models/embeddings"],
                    ConfigDocumentPaths = ["path_embeddings"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.VAE],
                    TargetRelativePaths = ["models/vae"],
                    ConfigDocumentPaths = ["path_vae"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.ApproxVAE],
                    TargetRelativePaths = ["models/vae_approx"],
                    ConfigDocumentPaths = ["path_vae_approx"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.Lora, SharedFolderType.LyCORIS],
                    TargetRelativePaths = ["models/loras"],
                    ConfigDocumentPaths = ["path_loras"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.InvokeClipVision],
                    TargetRelativePaths = ["models/clip_vision"],
                    ConfigDocumentPaths = ["path_clip_vision"]
                },
                new SharedFolderLayoutRule
                {
                    SourceTypes = [SharedFolderType.ControlNet],
                    TargetRelativePaths = ["models/controlnet"],
                    ConfigDocumentPaths = ["path_controlnet"]
                },
                new SharedFolderLayoutRule
                {
                    TargetRelativePaths = ["models/inpaint"],
                    ConfigDocumentPaths = ["path_inpaint"]
                },
                new SharedFolderLayoutRule
                {
                    TargetRelativePaths = ["models/prompt_expansion/fooocus_expansion"],
                    ConfigDocumentPaths = ["path_fooocus_expansion"]
                }
            ]
        };

    public override Dictionary<SharedOutputType, IReadOnlyList<string>> SharedOutputFolders =>
        new() { [SharedOutputType.Text2Img] = new[] { "outputs" } };

    public override IEnumerable<TorchVersion> AvailableTorchVersions =>
        new[] { TorchVersion.Cpu, TorchVersion.Cuda, TorchVersion.DirectMl, TorchVersion.Rocm };

    public override string MainBranch => "main";

    public override bool ShouldIgnoreReleases => true;

    public override string OutputFolderName => "outputs";

    public override PackageDifficulty InstallerSortOrder => PackageDifficulty.Simple;

    public override async Task InstallPackage(
        string installLocation,
        TorchVersion torchVersion,
        SharedFolderMethod selectedSharedFolderMethod,
        DownloadPackageVersionOptions versionOptions,
        IProgress<ProgressReport>? progress = null,
        Action<ProcessOutput>? onConsoleOutput = null
    )
    {
        var venvRunner = await SetupVenv(installLocation, forceRecreate: true).ConfigureAwait(false);
        venvRunner.EnvironmentVariables = SettingsManager.Settings.EnvironmentVariables;

        progress?.Report(new ProgressReport(-1f, "Installing requirements...", isIndeterminate: true));

        var pipArgs = new PipInstallArgs();

        if (torchVersion == TorchVersion.DirectMl)
        {
            pipArgs = pipArgs.WithTorchDirectML();
        }
        else
        {
            pipArgs = pipArgs
                .WithTorch("==2.1.0")
                .WithTorchVision("==0.16.0")
                .WithTorchExtraIndex(
                    torchVersion switch
                    {
                        TorchVersion.Cpu => "cpu",
                        TorchVersion.Cuda => "cu121",
                        TorchVersion.Rocm => "rocm5.6",
                        _ => throw new ArgumentOutOfRangeException(nameof(torchVersion), torchVersion, null)
                    }
                );
        }

        var requirements = new FilePath(installLocation, "requirements_versions.txt");

        pipArgs = pipArgs.WithParsedFromRequirementsTxt(
            await requirements.ReadAllTextAsync().ConfigureAwait(false),
            excludePattern: "torch"
        );

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

        void HandleConsoleOutput(ProcessOutput s)
        {
            onConsoleOutput?.Invoke(s);

            if (s.Text.Contains("Use the app with", StringComparison.OrdinalIgnoreCase))
            {
                var regex = new Regex(@"(https?:\/\/)([^:\s]+):(\d+)");
                var match = regex.Match(s.Text);
                if (match.Success)
                {
                    WebUrl = match.Value;
                }
                OnStartupComplete(WebUrl);
            }
        }

        void HandleExit(int i)
        {
            Debug.WriteLine($"Venv process exited with code {i}");
            OnExit(i);
        }

        var args = $"\"{Path.Combine(installedPackagePath, command)}\" {arguments}";

        VenvRunner?.RunDetached(args.TrimEnd(), HandleConsoleOutput, HandleExit);
    }

    public override Task SetupModelFolders(
        DirectoryPath installDirectory,
        SharedFolderMethod sharedFolderMethod
    )
    {
        return sharedFolderMethod switch
        {
            SharedFolderMethod.Symlink
                => base.SetupModelFolders(installDirectory, SharedFolderMethod.Symlink),
            SharedFolderMethod.Configuration
                => SharedFoldersConfigHelper.UpdateJsonConfigFileForSharedAsync(
                    SharedFolderLayout,
                    installDirectory,
                    SettingsManager.ModelsDirectory
                ),
            SharedFolderMethod.None => Task.CompletedTask,
            _ => throw new ArgumentOutOfRangeException(nameof(sharedFolderMethod), sharedFolderMethod, null)
        };
    }

    public override Task RemoveModelFolderLinks(
        DirectoryPath installDirectory,
        SharedFolderMethod sharedFolderMethod
    )
    {
        return sharedFolderMethod switch
        {
            SharedFolderMethod.Symlink => base.RemoveModelFolderLinks(installDirectory, sharedFolderMethod),
            SharedFolderMethod.Configuration
                => SharedFoldersConfigHelper.UpdateJsonConfigFileForDefaultAsync(
                    SharedFolderLayout,
                    installDirectory
                ),
            SharedFolderMethod.None => Task.CompletedTask,
            _ => throw new ArgumentOutOfRangeException(nameof(sharedFolderMethod), sharedFolderMethod, null)
        };
    }
}
