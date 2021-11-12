// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Runtime.Versioning;
using NuGet.Frameworks;
using NuGet.VisualStudio.Implementation.Resources;
using NuGet.VisualStudio.Telemetry;

namespace NuGet.VisualStudio.Implementation.Extensibility
{
    [Export(typeof(IVsFrameworkParser))]
    [Export(typeof(IVsFrameworkParser2))]
    public class VsFrameworkParser : IVsFrameworkParser, IVsFrameworkParser2
    {
        private INuGetTelemetryProvider _telemetryProvider;

        [ImportingConstructor]
        public VsFrameworkParser(INuGetTelemetryProvider telemetryProvider)
        {
            _telemetryProvider = telemetryProvider;
        }

        public FrameworkName ParseFrameworkName(string shortOrFullName)
        {
            const string eventName = nameof(IVsFrameworkParser) + "." + nameof(ParseFrameworkName);
            NuGetExtensibilityEtw.EventSource.Write(eventName, NuGetExtensibilityEtw.StartEventOptions,
                new
                {
                    Framework = shortOrFullName
                });
            try
            {
                if (shortOrFullName == null)
                {
                    throw new ArgumentNullException(nameof(shortOrFullName));
                }

                try
                {
                    var nuGetFramework = NuGetFramework.Parse(shortOrFullName);
                    return new FrameworkName(nuGetFramework.DotNetFrameworkName);
                }
                catch (ArgumentException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _telemetryProvider.PostFault(exception, typeof(VsFrameworkParser).FullName);
                    throw;
                }
            }
            finally
            {
                NuGetExtensibilityEtw.EventSource.Write(eventName, NuGetExtensibilityEtw.StopEventOptions);
            }
        }

        public string GetShortFrameworkName(FrameworkName frameworkName)
        {
            const string eventName = nameof(IVsFrameworkParser) + "." + nameof(GetShortFrameworkName);
            NuGetExtensibilityEtw.EventSource.Write(eventName, NuGetExtensibilityEtw.StartEventOptions,
                new
                {
                    Framework = frameworkName.FullName
                });
            try
            {
                if (frameworkName == null)
                {
                    throw new ArgumentNullException(nameof(frameworkName));
                }

                try
                {
                    var nuGetFramework = NuGetFramework.ParseFrameworkName(
                        frameworkName.ToString(),
                        DefaultFrameworkNameProvider.Instance);

                    try
                    {
                        return nuGetFramework.GetShortFolderName();
                    }
                    catch (FrameworkException e)
                    {
                        // Wrap this exception for two reasons:
                        //
                        // 1) FrameworkException is not a .NET Framework type and therefore is not
                        //    recognized by other components in Visual Studio.
                        //
                        // 2) Changing our NuGet code to throw ArgumentException is not appropriate in
                        //    this case because the failure does not occur in a method that has arguments!
                        var message = string.Format(
                            CultureInfo.CurrentCulture,
                            VsResources.CouldNotGetShortFrameworkName,
                            frameworkName);
                        throw new ArgumentException(message, e);
                    }
                }
                catch (ArgumentException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _telemetryProvider.PostFault(exception, typeof(VsFrameworkParser).FullName);
                    throw;
                }
            }
            finally
            {
                NuGetExtensibilityEtw.EventSource.Write(eventName, NuGetExtensibilityEtw.StopEventOptions);
            }
        }

        public bool TryParse(string input, out IVsNuGetFramework nuGetFramework)
        {
            const string eventName = nameof(IVsFrameworkParser2) + "." + nameof(TryParse);
            NuGetExtensibilityEtw.EventSource.Write(eventName, NuGetExtensibilityEtw.StartEventOptions,
                new
                {
                    Framework = input
                });

            try
            {
                if (input == null)
                {
                    throw new ArgumentNullException(nameof(input));
                }

                try
                {
                    var framework = NuGetFramework.Parse(input);

                    var targetFrameworkMoniker = framework.DotNetFrameworkName;
                    var targetPlatformMoniker = framework.DotNetPlatformName;
                    string targetPlatforMinVersion = null;

                    nuGetFramework = new VsNuGetFramework(targetFrameworkMoniker, targetPlatformMoniker, targetPlatforMinVersion);
                    return framework.IsSpecificFramework;
                }
                catch (Exception exception)
                {
                    _telemetryProvider.PostFault(exception, typeof(VsFrameworkParser).FullName);
                    throw;
                }
            }
            finally
            {
                NuGetExtensibilityEtw.EventSource.Write(eventName, NuGetExtensibilityEtw.StopEventOptions);
            }
        }
    }
}
