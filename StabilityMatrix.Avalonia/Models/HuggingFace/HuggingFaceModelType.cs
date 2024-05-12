﻿using System.Text.Json.Serialization;
using StabilityMatrix.Core.Converters.Json;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Models;

namespace StabilityMatrix.Avalonia.Models.HuggingFace;

[JsonConverter(typeof(DefaultUnknownEnumConverter<HuggingFaceModelType>))]
public enum HuggingFaceModelType
{
    [Description("Base Models")]
    [ConvertTo<SharedFolderType>(SharedFolderType.StableDiffusion)]
    BaseModel,

    [Description("ControlNets (SD1.5)")]
    [ConvertTo<SharedFolderType>(SharedFolderType.ControlNet)]
    ControlNet,

    [Description("ControlNets (Diffusers SD1.5)")]
    [ConvertTo<SharedFolderType>(SharedFolderType.ControlNet)]
    DiffusersControlNet,

    [Description("ControlNets (SDXL)")]
    [ConvertTo<SharedFolderType>(SharedFolderType.ControlNet)]
    ControlNetXl,

    [Description("IP Adapters")]
    [ConvertTo<SharedFolderType>(SharedFolderType.IpAdapter)]
    IpAdapter,

    [Description("IP Adapters (Diffusers SD1.5)")]
    [ConvertTo<SharedFolderType>(SharedFolderType.InvokeIpAdapters15)]
    DiffusersIpAdapter,

    [Description("IP Adapters (Diffusers SDXL)")]
    [ConvertTo<SharedFolderType>(SharedFolderType.InvokeIpAdaptersXl)]
    DiffusersIpAdapterXl,

    [Description("CLIP Vision (Diffusers)")]
    [ConvertTo<SharedFolderType>(SharedFolderType.InvokeClipVision)]
    DiffusersClipVision,

    [Description("T2I Adapters")]
    [ConvertTo<SharedFolderType>(SharedFolderType.T2IAdapter)]
    T2IAdapter,

    [Description("T2I Adapters (Diffusers)")]
    [ConvertTo<SharedFolderType>(SharedFolderType.T2IAdapter)]
    DiffusersT2IAdapter,
}
