# Third-party notices

Wi-Fi 体检台本身采用 [MIT License](LICENSE)。下列组件仍分别适用其原始许可证；本项目的 MIT 许可证不会替代、缩减或重新许可这些第三方条款。

正式安装包会同时携带本文件和 [`licenses/`](licenses/) 中未经翻译的许可证与通知原文。版本或依赖变化时，维护者必须同步更新本清单和对应原文。

## Windows 运行时组件

| 组件 | 使用版本 | 许可证与版权通知 | 原文 |
| --- | --- | --- | --- |
| Avalonia、Avalonia.Desktop、Avalonia.Themes.Fluent、Avalonia.Win32、Avalonia.Skia、Avalonia.HarfBuzz、Avalonia.Native、Avalonia.X11、Avalonia.FreeDesktop、Avalonia.Remote.Protocol 及相关运行时包 | 12.1.0 | MIT；Copyright (c) AvaloniaUI OÜ。Avalonia 还包含来自 WPF、Silverlight Toolkit、Wayland、Chromium、Flutter、Reactive Extensions 等项目的代码或通知 | [Avalonia license](licenses/Avalonia-LICENSE.md)、[Avalonia notices](licenses/Avalonia-NOTICE.md) |
| Avalonia.Fonts.Inter / Inter font family | 12.1.0 / bundled font files | Avalonia 包装层为 MIT；字体为 SIL Open Font License 1.1，Copyright (c) 2016 The Inter Project Authors | [Inter OFL 1.1](licenses/Inter-OFL-1.1.txt) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT；Copyright (c) .NET Foundation and Contributors；NuGet 包还携带 DeferredEvents、MVVM Light、ComputeSharp 等上游通知 | [CommunityToolkit.Mvvm license](licenses/CommunityToolkit-Mvvm-LICENSE.md)、[complete package notices](licenses/CommunityToolkit-Mvvm-THIRD-PARTY-NOTICES.txt) |
| FluentIcons.Avalonia、FluentIcons.Common、FluentIcons.Resources.Avalonia | 2.1.333 | MIT；Copyright (c) 2022 davidxuang | [FluentIcons license](licenses/FluentIcons-LICENSE.txt) |
| Microsoft Fluent UI System Icons | bundled by FluentIcons | MIT；Copyright (c) 2020 Microsoft Corporation | [Fluent UI System Icons license](licenses/FluentUI-System-Icons-LICENSE.txt) |
| SkiaSharp / SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT binding; Copyright (c) 2015-2016 Xamarin, Inc. and Copyright (c) 2017-2018 Microsoft Corporation. Native Skia contains separately licensed upstream material | [binding license](licenses/SkiaSharp-HarfBuzzSharp-LICENSE.txt)、[complete upstream notices](licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt) |
| HarfBuzzSharp / HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT binding; Copyright (c) Microsoft Corporation. Native HarfBuzz contains separately licensed upstream material | [binding license](licenses/SkiaSharp-HarfBuzzSharp-LICENSE.txt)、[complete upstream notices](licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt) |
| Avalonia.Angle.Windows.Natives / ANGLE | 2.1.27548.20260419 | BSD 3-Clause; Copyright 2018 The ANGLE Project Authors | [ANGLE license](licenses/ANGLE-BSD-3-Clause.txt) |
| MicroCom.Runtime | 0.11.6 | MIT; Copyright 2021 Nikita Tsukanov | [MicroCom license](licenses/MicroCom-LICENSE.txt) |
| Tmds.DBus.Protocol | 0.94.1 | MIT; Copyright Alp Toker, other contributors, and Tom Deseyn | [Tmds.DBus license](licenses/Tmds.DBus-COPYING.txt) |
| Microsoft.Extensions.DependencyInjection.Abstractions and Microsoft.Extensions.Logging.Abstractions | 8.0.0 | MIT; Copyright (c) Microsoft Corporation | [.NET MIT license text](licenses/dotnet-LICENSE.txt)，本行保留对应包的版权通知 |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | MIT; Copyright (c) Microsoft Corporation | [RecyclableMemoryStream license](licenses/Microsoft.IO.RecyclableMemoryStream-LICENSE.txt) |
| .NET self-contained runtime and host | 10.0.10 | MIT; package metadata Copyright (c) Microsoft Corporation，license text Copyright (c) .NET Foundation and Contributors. The runtime contains separately licensed upstream material | [.NET license](licenses/dotnet-LICENSE.txt)、[complete .NET notices](licenses/dotnet-THIRD-PARTY-NOTICES.txt) |

SkiaSharp / HarfBuzzSharp 的完整上游通知包含 ANGLE、HarfBuzz、Skia、libpng、Expat、FreeType、ICU、libjpeg-turbo、libwebp、zlib，以及 Adobe DNG SDK 等材料的原始条款。该文件按 NuGet 正式包原样保留；其中的第三方材料没有被本项目重新许可为 MIT。

## 构建、测试与安装器

这些组件用于构建或测试，通常不作为独立运行库安装，但源码分发和发布流程仍保留其许可信息。

| 组件 | 使用版本 | 许可证与版权通知 | 原文 |
| --- | --- | --- | --- |
| Avalonia.BuildServices | 11.3.2 | MIT; Copyright 2023-2025 The AvaloniaUI Project | [Avalonia license](licenses/Avalonia-LICENSE.md) |
| Microsoft.NET.Test.Sdk / VSTest | 18.8.1 | MIT; Copyright (c) Microsoft Corporation | [VSTest license](licenses/VSTest-LICENSE.txt) |
| xUnit.net and xunit.runner.visualstudio | 2.9.3 / 3.1.5 | Apache License 2.0; Copyright (c) .NET Foundation. xUnit 的上游通知还列出少量 MIT 代码 | [Apache 2.0](licenses/Apache-2.0.txt)、[xUnit notices](licenses/xUnit-LICENSE.txt) |
| Coverlet collector | 10.0.1 | MIT; Copyright (c) 2018 Toni Solarin-Sodara | [Coverlet license](licenses/Coverlet-LICENSE.txt) |
| NSIS | 3.12.0 | zlib/libpng license；压缩模块还包括 bzip2 和 Common Public License 1.0，LZMA 模块适用上游 special exception | [NSIS COPYING](licenses/NSIS-COPYING.txt) |
| actions/checkout, actions/setup-dotnet, actions/upload-artifact | pinned Git commits corresponding to v7 / v5 / v7 | MIT; Copyright GitHub, Inc. and contributors | 各 Action 的上游仓库许可证 |

## macOS 系统组件

macOS 目标没有 Swift Package 第三方依赖。应用链接 Foundation、AppKit、SwiftUI、Charts、CoreWLAN、CoreLocation 和系统 Swift runtime；这些属于 macOS / Xcode 系统库，不复制到本仓库。界面中的 SF Symbols 由系统在运行时提供，本项目没有导出或打包 SF Symbols 资源。

## 项目资产

`Assets/AppIcon.png` 是为本项目创作的图标；`.icns`、iconset、Windows PNG / ICO 为它的派生格式。`Windows/design-reference/` 中的图片是本项目界面截图。维护者将这些项目自有资产与源码一并按根目录 MIT License 发布。

## 上游来源

- Avalonia: <https://github.com/AvaloniaUI/Avalonia>
- Inter: <https://github.com/rsms/inter>
- .NET Community Toolkit: <https://github.com/CommunityToolkit/dotnet>
- FluentIcons: <https://github.com/davidxuang/FluentIcons>
- Microsoft Fluent UI System Icons: <https://github.com/microsoft/fluentui-system-icons>
- SkiaSharp / HarfBuzzSharp: <https://github.com/mono/SkiaSharp>
- Avalonia ANGLE: <https://github.com/AvaloniaUI/angle>
- MicroCom: <https://github.com/kekekeks/MicroCom>
- Tmds.DBus: <https://github.com/tmds/Tmds.DBus>
- .NET runtime 10.0.10: <https://github.com/dotnet/dotnet/tree/f7d90799ce4ef09a0bb257852a57248d2a8fb8dd>
- xUnit.net: <https://github.com/xunit/xunit>
- NSIS: <https://github.com/NSIS-Dev/nsis>
