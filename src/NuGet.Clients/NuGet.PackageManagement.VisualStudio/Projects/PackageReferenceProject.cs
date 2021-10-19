// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.PackageManagement.VisualStudio.Utility;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.ProjectModel;
using FrameworkRIDKey = System.Tuple<NuGet.Frameworks.NuGetFramework, string>;
using TransitiveEntry = System.Collections.Generic.IDictionary<System.Tuple<NuGet.Frameworks.NuGetFramework, string>, System.Collections.Generic.IList<NuGet.Packaging.PackageReference>>;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// An implementation of <see cref="NuGetProject"/> that interfaces with VS project APIs to coordinate
    /// packages in a package reference style project.
    /// </summary>
    public abstract class PackageReferenceProject : BuildIntegratedNuGetProject
    {
        private protected readonly Dictionary<string, TransitiveEntry> TransitiveOriginsCache = new();

        private readonly protected string _projectName;
        private readonly protected string _projectUniqueName;
        private readonly protected string _projectFullPath;

        private protected DateTime _lastTimeAssetsModified;
        private protected WeakReference<PackageSpec> _lastPackageSpec;

        private protected WeakReference<IList<LockFileTarget>> _lastTargetsList;

        protected PackageReferenceProject(
            string projectName,
            string projectUniqueName,
            string projectFullPath)
        {
            _projectName = projectName;
            _projectUniqueName = projectUniqueName;
            _projectFullPath = projectFullPath;
        }

        public override async Task<string> GetAssetsFilePathAsync()
        {
            return await GetAssetsFilePathAsync(shouldThrow: true);
        }

        public override async Task<string> GetAssetsFilePathOrNullAsync()
        {
            return await GetAssetsFilePathAsync(shouldThrow: false);
        }

        private protected abstract Task<string> GetAssetsFilePathAsync(bool shouldThrow);

        public override string ProjectName => _projectName;

        public override async Task<IReadOnlyList<PackageSpec>> GetPackageSpecsAsync(DependencyGraphCacheContext context)
        {
            (IReadOnlyList<PackageSpec> dgSpec, IReadOnlyList<IAssetsLogMessage> _) = await GetPackageSpecsAndAdditionalMessagesAsync(context);
            return dgSpec;
        }

        public abstract Task<ProjectPackages> GetInstalledAndTransitivePackagesAsync(CancellationToken token);

        private protected IEnumerable<PackageReference> GetPackageReferences(IEnumerable<LibraryDependency> libraries, NuGetFramework targetFramework, Dictionary<string, ProjectInstalledPackage> installedPackages, IList<LockFileTarget> targets)
        {
            return libraries
                .Where(library => library.LibraryRange.TypeConstraint == LibraryDependencyTarget.Package)
                .Select(library => new BuildIntegratedPackageReference(library, targetFramework, GetPackageReferenceUtility.UpdateResolvedVersion(library, targetFramework, targets, installedPackages)));
        }

        private protected IReadOnlyList<PackageReference> GetTransitivePackageReferences(NuGetFramework targetFramework, Dictionary<string, ProjectInstalledPackage> installedPackages, Dictionary<string, ProjectInstalledPackage> transitivePackages, IList<LockFileTarget> targets)
        {
            // If the assets files has not been updated, return the cached transitive packages
            if (targets == null)
            {
                return transitivePackages
                    .Select(package => new PackageReference(package.Value.InstalledPackage, targetFramework))
                    .ToList();
            }
            else
            {
                return targets
                    .SelectMany(target => target.Libraries)
                    .Where(library => library.Type == LibraryType.Package)
                    .SelectMany(library => GetPackageReferenceUtility.UpdateTransitiveDependencies(library, targetFramework, targets, installedPackages, transitivePackages))
                    .Select(packageIdentity => new PackageReference(packageIdentity, targetFramework))
                    .ToList();
            }
        }

        /// <summary>
        /// Get All Installed packages that transitively install a given transitive package in this project
        /// </summary>
        /// <param name="transitivePackage">Identity of given transtive package</param>
        /// <param name="ct">Cancellation Token</param>
        /// <returns>A dictionary, indexed by Framework/Runtime-ID with all top (installed)
        /// packages that depends on given transitive package, or <c>null</c> if none found</returns>
        /// <remarks>Computes all transitive origings for each Framework/Runtime-ID combiation. Runtime-ID can be <c>null</c>.
        /// Transitive origins are calculated using a Depth First Search algorithm on all direct dependencies exhaustively</remarks>
        internal async ValueTask<TransitiveEntry> GetTransitivePackageOriginAsync(PackageIdentity transitivePackage, CancellationToken ct)
        {
            /* Pseudocode
            1. Get project restore graph

            2. If it is cached
               2.1 Look for a transitive cached entry
               2.2 If found, return that entry

            Otherwise:

            3. For each target framework graph (Framework, RID)-pair:
              3.1 For each direct dependency d:
                  3.1.1 Do DFS to mark d as a transitive origin over all transitive dependencies found

            4. return cached result for specific transitive dependency
            */

            ct.ThrowIfCancellationRequested();

            RestoreGraphRead reading = await GetFullRestoreGraphAsync(ct);
            if (reading.IsCacheHit)
            {
                // Assets file has not changed, look at transtive origin cache
                var cacheEntry = GetCachedTransitiveOrigin(transitivePackage);
                if (cacheEntry != null)
                {
                    return cacheEntry;
                }
            }
            else
            {
                // Assets file changed, recompute transitive origins
                CleanCache();
            }

            // Otherwise, find all Transitive origin and update cache
            var memory = new Dictionary<PackageIdentity, bool?>();

            IList<LockFileTarget> targetsList;
            if (reading.TargetsList != null)
            {
                targetsList = reading.TargetsList;
            }
            else
            {
                targetsList = await GetTargetsListAsync(ct);
            }

            var pkgs = await GetInstalledAndTransitivePackagesAsync(ct);

            // 3. For each target framework graph (Framework, RID)-pair:
            foreach (var targetFxGraph in targetsList)
            {
                var key = Tuple.Create(targetFxGraph.TargetFramework, targetFxGraph.RuntimeIdentifier);

                foreach (var directPkg in pkgs.InstalledPackages) // 3.1 For each direct dependency d:
                {
                    memory.Clear();
                    MarkTransitiveOrigin(directPkg, directPkg.PackageIdentity, targetFxGraph, memory, key);
                }
            }

            return GetCachedTransitiveOrigin(transitivePackage);
        }

        /// <summary>
        /// Returns <see cref="PackageSpec"/> and all targets (dependency graph) found in assets file (project.assets.json)
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>An <see cref="RestoreGraphRead"/> object</returns>
        /// <remarks>Projects need to be NuGet-restored before calling this function. Assets file reading happens in background</remarks>
        internal async Task<RestoreGraphRead> GetFullRestoreGraphAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string assetsFilePath = await GetAssetsFilePathAsync();
            var assets = new FileInfo(assetsFilePath);

            PackageSpec currentPackageSpec = await GetPackageSpecAsync(token);
            PackageSpec cachedPackageSpec = null;
            bool cacheHitPackageSpec = _lastPackageSpec != null && _lastPackageSpec.TryGetTarget(out cachedPackageSpec);

            IList<LockFileTarget> cachedTargetsList = null;
            bool cacheHitTargets = _lastTargetsList != null && _lastTargetsList.TryGetTarget(out cachedTargetsList);

            bool cacheMissAssets = (assets.Exists && assets.LastWriteTimeUtc > _lastTimeAssetsModified);
            bool isCacheHit = false;

            IList<LockFileTarget> currentTargetsList = null;

            if (cacheMissAssets || IsPackageSpecDifferent(currentPackageSpec, cachedPackageSpec))
            {
                if (assets.Exists)
                {
                    currentTargetsList = await GetTargetsListAsync(assetsFilePath, token);
                }

                _lastTimeAssetsModified = assets.LastWriteTimeUtc;
                _lastPackageSpec = new WeakReference<PackageSpec>(currentPackageSpec);
                _lastTargetsList = new WeakReference<IList<LockFileTarget>>(currentTargetsList);
            }
            else if (cacheHitPackageSpec && cacheHitTargets && cachedPackageSpec != null && cachedTargetsList != null)
            {
                isCacheHit = true;
                currentTargetsList = cachedTargetsList;
            }

            return new RestoreGraphRead(currentPackageSpec, currentTargetsList, isCacheHit);
        }

        /// <summary>
        /// Obtains targets section from project assets file (project.assets.json)
        /// </summary>
        /// <param name="ct">Cancellation token for async operation</param>
        /// <returns>A lis of dependencies, indexed by framework/RID</returns>
        /// <remarks>Assets file reading occurs in a background thread</remarks>
        /// <seealso cref="GetAssetsFilePathAsync"/>
        private async ValueTask<IList<LockFileTarget>> GetTargetsListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string assetsFilePath = await GetAssetsFilePathAsync();
            return await GetTargetsListAsync(assetsFilePath, ct);
        }

        /// <summary>
        /// Obtains targets section from project assets file (project.assets.json)
        /// </summary>
        /// <param name="assetsFilePath">File path to project.assets.json</param>
        /// <param name="ct">Cancellation token for async operation</param>
        /// <returns>A lis of dependencies, indexed by framework/RID</returns>
        /// <remarks>Assets file reading occurs in a background thread</remarks>
        private async ValueTask<IList<LockFileTarget>> GetTargetsListAsync(string assetsFilePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            await TaskScheduler.Default;
            LockFile lockFile = LockFileUtilities.GetLockFile(assetsFilePath, NullLogger.Instance);

            return lockFile?.Targets;
        }

        /// <summary>
        /// Runs Depth First Search recursively to mark current and dependend nodes with top dependency
        /// </summary>
        /// <param name="top">Top, Direct dependency</param>
        /// <param name="current">Current package/node to visit</param>
        /// <param name="graph">Package dependency graph, from assets file</param>
        /// <param name="memory">Dictionary to remember visited nodes</param>
        /// <param name="fxRidEntry">Framework/Runtime-ID associated with current <paramref name="graph"/></param>
        private void MarkTransitiveOrigin(PackageReference top, PackageIdentity current, LockFileTarget graph, Dictionary<PackageIdentity, bool?> memory, FrameworkRIDKey fxRidEntry)
        {
            LockFileTargetLibrary node = graph
                .Libraries
                .Where(x => x.Name.ToLowerInvariant() == current.Id.ToLowerInvariant()
                        && x.Version.Equals(current.Version) && x.Type == "package")
                .FirstOrDefault();

            if (node != default)
            {
                memory[current] = true; // visited

                // Update cache
                TransitiveEntry cachedEntry = GetCachedTransitiveOrigin(current);
                if (cachedEntry == null)
                {
                    cachedEntry = new Dictionary<FrameworkRIDKey, IList<PackageReference>>
                    {
                        [fxRidEntry] = new List<PackageReference>()
                    };

                }
                if (!cachedEntry.ContainsKey(fxRidEntry))
                {
                    cachedEntry[fxRidEntry] = new List<PackageReference>();
                }
                if (!cachedEntry[fxRidEntry].Contains(top))
                {
                    cachedEntry[fxRidEntry].Add(top);
                }
                SetCachedTransitiveOrigin(current, cachedEntry);

                foreach (PackageDependency dep in node.Dependencies)
                {
                    var pkgChild = new PackageIdentity(dep.Id, dep.VersionRange.MinVersion);

                    if (!memory.ContainsKey(pkgChild))
                    {
                        MarkTransitiveOrigin(top, pkgChild, graph, memory, fxRidEntry);
                    }
                }
            }
        }

        /// <summary>
        /// Generates a cache key for Transitive Originas cache
        /// </summary>
        /// <param name="transitivePackage"></param>
        /// <returns>A string with given key</returns>
        /// <seealso cref="GetCachedTransitiveOrigin(PackageIdentity)"/>
        /// <seealso cref="SetCachedTransitiveOrigin(PackageIdentity, TransitiveEntry)"/>
        internal string GetTransitiveCacheKey(PackageIdentity transitivePackage)
        {
            return _projectUniqueName + "/" + transitivePackage.Id.ToLowerInvariant() + "." + transitivePackage.Version.ToNormalizedString();
        }

        /// <summary>
        /// Obtains cached entry for a given transitive package
        /// </summary>
        /// <param name="transitivePackage">Identity of transitive package</param>
        /// <returns>A <see cref="TransitiveEntry"/> object, or <c>null</c> if not found</returns>
        /// <seealso cref="ClearCachedTransitiveOrigins"/>
        /// <seealso cref="SetCachedTransitiveOrigin(PackageIdentity, TransitiveEntry)"/>
        internal TransitiveEntry GetCachedTransitiveOrigin(PackageIdentity transitivePackage)
        {
            string key = GetTransitiveCacheKey(transitivePackage);

            if (TransitiveOriginsCache.ContainsKey(key))
            {
                return TransitiveOriginsCache[key];
            }

            return null;
        }

        /// <summary>
        /// Replaces cached entry for a given transitive package with a <see cref="TransitiveEntry"/>
        /// </summary>
        /// <param name="transitivePackage">Identity of transitive package</param>
        /// <param name="origins">Packages identified as package origins</param>
        /// <seealso cref="ClearCachedTransitiveOrigins"/>
        /// <seealso cref="GetCachedTransitiveOrigin(PackageIdentity)"/>
        internal void SetCachedTransitiveOrigin(PackageIdentity transitivePackage, TransitiveEntry origins)
        {
            string key = GetTransitiveCacheKey(transitivePackage);
            TransitiveOriginsCache[key] = origins;
        }

        /// <summary>
        /// Clears Transitive Origins cache
        /// </summary>
        /// <seealso cref="GetCachedTransitiveOrigin(PackageIdentity)"/>
        /// <seealso cref="SetCachedTransitiveOrigin(PackageIdentity, TransitiveEntry)"/>
        internal void ClearCachedTransitiveOrigins()
        {
            TransitiveOriginsCache.Clear();
        }

        /// <summary>
        /// Obtains <see cref="PackageSpec"/> object from assets file from disk
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <remarks>Each project implementation has its own way for gathering assets file</remarks>
        /// <returns>A <see cref="PackageSpec"/> filled from assets file on disk</returns>
        internal abstract ValueTask<PackageSpec> GetPackageSpecAsync(CancellationToken ct);

        /// <summary>
        /// Decides wether cached <see cref="PackageSpec"/> differs from assets file on disk
        /// </summary>
        /// <param name="actual">A <see cref="PackageSpec"/> read from disk</param>
        /// <param name="cached">Cached <see cref="PackageSpec"/></param>
        /// <returns><c>true</c> if current <see cref="PackageSpec"/> differs from cached objects</returns>
        internal abstract bool IsPackageSpecDifferent(PackageSpec actual, PackageSpec cached);

        /// <summary>
        /// Clears Cached Transitive package prigins, Installed packages and Transitive packages
        /// </summary>
        internal abstract void CleanCache();
    }
}
