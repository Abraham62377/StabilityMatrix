﻿using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Helper.Cache;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Core.Models.Packages;

[Singleton(typeof(BasePackage))]
public class Reforge(
    IGithubApiCache githubApi,
    ISettingsManager settingsManager,
    IDownloadService downloadService,
    IPrerequisiteHelper prerequisiteHelper
) : SDWebForge(githubApi, settingsManager, downloadService, prerequisiteHelper)
{
    public override string Name => "reforge";
    public override string Author => "Panchovix";
    public override string RepositoryName => "stable-diffusion-webui-reForge";
    public override string DisplayName { get; set; } = "Stable Diffusion WebUI reForge";
    public override string Blurb =>
        "Stable Diffusion WebUI reForge is a platform on top of Stable Diffusion WebUI (based on Gradio) to make development easier, optimize resource management, speed up inference, and study experimental features.";
    public override string LicenseUrl =>
        "https://github.com/Panchovix/stable-diffusion-webui-reForge/blob/main/LICENSE.txt";
    public override Uri PreviewImageUri =>
        new(
            "https://github.com/lllyasviel/stable-diffusion-webui-forge/assets/19834515/de1a2d05-344a-44d7-bab8-9ecc0a58a8d3"
        );

    public override PackageDifficulty InstallerSortOrder => PackageDifficulty.ReallyRecommended;
}