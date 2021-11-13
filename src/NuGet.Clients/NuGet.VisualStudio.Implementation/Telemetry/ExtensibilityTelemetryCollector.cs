// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using NuGet.Common;
using NuGet.VisualStudio.Contracts;
using NuGet.VisualStudio.Implementation.Extensibility;

namespace NuGet.VisualStudio.Telemetry
{
    internal sealed class ExtensibilityTelemetryCollector : IDisposable
    {
        IReadOnlyDictionary<string, Count> _counts;

        private class Count
        {
            public int Value;
        }

        private ExtensibilityEventListener _eventListener;

        public ExtensibilityTelemetryCollector()
        {
            _eventListener = new ExtensibilityEventListener(this);
            _counts = new Dictionary<string, Count>()
            {
                // INuGetProjectService
                [nameof(INuGetProjectService) + "." + nameof(INuGetProjectService.GetInstalledPackagesAsync)] = new Count(),

                // IVsFrameworkCompatibility
                [nameof(IVsFrameworkCompatibility) + "." + nameof(IVsFrameworkCompatibility.GetNetStandardFrameworks)] = new Count(),
                [nameof(IVsFrameworkCompatibility) + "." + nameof(IVsFrameworkCompatibility.GetFrameworksSupportingNetStandard)] = new Count(),
                [nameof(IVsFrameworkCompatibility) + "." + nameof(IVsFrameworkCompatibility.GetNearest)] = new Count(),

                // IVsFrameworkCompatibility2
                [nameof(IVsFrameworkCompatibility2) + "." + nameof(IVsFrameworkCompatibility2.GetNearest)] = new Count(),

                // IVsFrameworkCompatibility2
                [nameof(IVsFrameworkCompatibility3) + ".GetNearest`2"] = new Count(),
                [nameof(IVsFrameworkCompatibility3) + ".GetNearest`3"] = new Count(),

                // IVsFrameworkParser
                [nameof(IVsFrameworkParser) + "." + nameof(IVsFrameworkParser.ParseFrameworkName)] = new Count(),
                [nameof(IVsFrameworkParser) + "." + nameof(IVsFrameworkParser.GetShortFrameworkName)] = new Count(),

                // IVsFrameworkParser2
                [nameof(IVsFrameworkParser2) + "." + nameof(IVsFrameworkParser2.TryParse)] = new Count(),

                // IVsGlobalPackagesInitScriptExecutor
                [nameof(IVsGlobalPackagesInitScriptExecutor) + "." + nameof(IVsGlobalPackagesInitScriptExecutor.ExecuteInitScriptAsync)] = new Count(),

                // IVsPackageInstaller
                [nameof(IVsPackageInstaller) + "." + nameof(IVsPackageInstaller.InstallPackage) + ".1"] = new Count(),
                [nameof(IVsPackageInstaller) + "." + nameof(IVsPackageInstaller.InstallPackage) + ".2"] = new Count(),
                [nameof(IVsPackageInstaller) + "." + nameof(IVsPackageInstaller.InstallPackagesFromRegistryRepository) + ".1"] = new Count(),
                [nameof(IVsPackageInstaller) + "." + nameof(IVsPackageInstaller.InstallPackagesFromRegistryRepository) + ".2"] = new Count(),
                [nameof(IVsPackageInstaller) + "." + nameof(IVsPackageInstaller.InstallPackagesFromVSExtensionRepository) + ".1"] = new Count(),
                [nameof(IVsPackageInstaller) + "." + nameof(IVsPackageInstaller.InstallPackagesFromVSExtensionRepository) + ".2"] = new Count(),

                // IVsPackageInstaller2
                [nameof(IVsPackageInstaller2) + "." + nameof(IVsPackageInstaller2.InstallLatestPackage)] = new Count(),

                // IVsPackageInstallerEvents
                [nameof(IVsPackageInstallerEvents) + "." + nameof(IVsPackageInstallerEvents.PackageInstalled)] = new Count(),
                [nameof(IVsPackageInstallerEvents) + "." + nameof(IVsPackageInstallerEvents.PackageInstalling)] = new Count(),
                [nameof(IVsPackageInstallerEvents) + "." + nameof(IVsPackageInstallerEvents.PackageReferenceAdded)] = new Count(),
                [nameof(IVsPackageInstallerEvents) + "." + nameof(IVsPackageInstallerEvents.PackageReferenceRemoved)] = new Count(),
                [nameof(IVsPackageInstallerEvents) + "." + nameof(IVsPackageInstallerEvents.PackageUninstalled)] = new Count(),
                [nameof(IVsPackageInstallerEvents) + "." + nameof(IVsPackageInstallerEvents.PackageUninstalling)] = new Count(),

                // IVsPackageInstallerProjectEvents
                [nameof(IVsPackageInstallerProjectEvents) + "." + nameof(IVsPackageInstallerProjectEvents.BatchStart)] = new Count(),
                [nameof(IVsPackageInstallerProjectEvents) + "." + nameof(IVsPackageInstallerProjectEvents.BatchEnd)] = new Count(),
            };
        }

        public void Dispose()
        {
            _eventListener?.Dispose();
            _eventListener = null;

            GC.SuppressFinalize(this);
        }

        public TelemetryEvent ToTelemetryEvent()
        {
            TelemetryEvent data = new("extensibility");

            foreach ((string api, Count count) in _counts)
            {
                data[api] = count.Value;
            }

            return data;
        }

        private class ExtensibilityEventListener : EventListener
        {
            private ExtensibilityTelemetryCollector _collector;

            public ExtensibilityEventListener(ExtensibilityTelemetryCollector collector)
            {
                _collector = collector;
            }

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                if (eventSource.Name == "NuGet-VS-Extensibility")
                {
                    EnableEvents(eventSource, EventLevel.LogAlways);
                }
                Debug.WriteLine(nameof(ExtensibilityEventListener) + " found " + eventSource.Name);
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (eventData.Opcode == EventOpcode.Start || eventData.Opcode == NuGetExtensibilityEtw.CustomOpcodes.Add)
                {
                    if (_collector._counts.TryGetValue(eventData.EventName, out Count count))
                    {
                        Interlocked.Increment(ref count.Value);
                    }
                    else
                    {
                        Debug.Assert(false, "VS Extensibility API without counter");
                    }
                }
            }
        }
    }
}