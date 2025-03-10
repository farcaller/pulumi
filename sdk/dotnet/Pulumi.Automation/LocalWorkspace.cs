﻿// Copyright 2016-2021, Pulumi Corporation

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pulumi.Automation.Commands;
using Pulumi.Automation.Serialization;

namespace Pulumi.Automation
{
    /// <summary>
    /// LocalWorkspace is a default implementation of the Workspace interface.
    /// <para/>
    /// A Workspace is the execution context containing a single Pulumi project, a program,
    /// and multiple stacks.Workspaces are used to manage the execution environment,
    /// providing various utilities such as plugin installation, environment configuration
    /// ($PULUMI_HOME), and creation, deletion, and listing of Stacks.
    /// <para/>
    /// LocalWorkspace relies on Pulumi.yaml and Pulumi.{stack}.yaml as the intermediate format
    /// for Project and Stack settings.Modifying ProjectSettings will
    /// alter the Workspace Pulumi.yaml file, and setting config on a Stack will modify the Pulumi.{stack}.yaml file.
    /// This is identical to the behavior of Pulumi CLI driven workspaces.
    /// <para/>
    /// If not provided a working directory - causing LocalWorkspace to create a temp directory,
    /// than the temp directory will be cleaned up on <see cref="Dispose"/>.
    /// </summary>
    public sealed class LocalWorkspace : Workspace
    {
        private readonly LocalSerializer _serializer = new LocalSerializer();
        private readonly bool _ownsWorkingDir;
        private readonly Task _readyTask;

        /// <inheritdoc/>
        public override string WorkDir { get; }

        /// <inheritdoc/>
        public override string? PulumiHome { get; }

        /// <inheritdoc/>
        public override string? SecretsProvider { get; }

        /// <inheritdoc/>
        public override PulumiFn? Program { get; set; }

        /// <inheritdoc/>
        public override IDictionary<string, string>? EnvironmentVariables { get; set; }

        /// <summary>
        /// Creates a workspace using the specified options. Used for maximal control and
        /// customization of the underlying environment before any stacks are created or selected.
        /// </summary>
        /// <param name="options">Options used to configure the workspace.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static async Task<LocalWorkspace> CreateAsync(
            LocalWorkspaceOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var ws = new LocalWorkspace(
                new LocalPulumiCmd(),
                options,
                cancellationToken);
            await ws._readyTask.ConfigureAwait(false);
            return ws;
        }

        /// <summary>
        /// Creates a Stack with a <see cref="LocalWorkspace"/> utilizing the specified
        /// inline (in process) <see cref="LocalWorkspaceOptions.Program"/>. This program
        /// is fully debuggable and runs in process. If no <see cref="LocalWorkspaceOptions.ProjectSettings"/>
        /// option is specified, default project settings will be created on behalf of the user. Similarly, unless a
        /// <see cref="LocalWorkspaceOptions.WorkDir"/> option is specified, the working directory will default
        /// to a new temporary directory provided by the OS.
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with an inline <see cref="PulumiFn"/> program
        ///     that runs in process, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        public static Task<WorkspaceStack> CreateStackAsync(InlineProgramArgs args)
            => CreateStackAsync(args, default);

        /// <summary>
        /// Creates a Stack with a <see cref="LocalWorkspace"/> utilizing the specified
        /// inline (in process) <see cref="LocalWorkspaceOptions.Program"/>. This program
        /// is fully debuggable and runs in process. If no <see cref="LocalWorkspaceOptions.ProjectSettings"/>
        /// option is specified, default project settings will be created on behalf of the user. Similarly, unless a
        /// <see cref="LocalWorkspaceOptions.WorkDir"/> option is specified, the working directory will default
        /// to a new temporary directory provided by the OS.
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with an inline <see cref="PulumiFn"/> program
        ///     that runs in process, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static Task<WorkspaceStack> CreateStackAsync(InlineProgramArgs args, CancellationToken cancellationToken)
            => CreateStackHelperAsync(args, WorkspaceStack.CreateAsync, cancellationToken);

        /// <summary>
        /// Creates a Stack with a <see cref="LocalWorkspace"/> utilizing the local Pulumi CLI program
        /// from the specified <see cref="LocalWorkspaceOptions.WorkDir"/>. This is a way to create drivers
        /// on top of pre-existing Pulumi programs. This Workspace will pick up any available Settings
        /// files(Pulumi.yaml, Pulumi.{stack}.yaml).
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with a pre-configured Pulumi CLI program that
        ///     already exists on disk, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        public static Task<WorkspaceStack> CreateStackAsync(LocalProgramArgs args)
            => CreateStackAsync(args, default);

        /// <summary>
        /// Creates a Stack with a <see cref="LocalWorkspace"/> utilizing the local Pulumi CLI program
        /// from the specified <see cref="LocalWorkspaceOptions.WorkDir"/>. This is a way to create drivers
        /// on top of pre-existing Pulumi programs. This Workspace will pick up any available Settings
        /// files(Pulumi.yaml, Pulumi.{stack}.yaml).
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with a pre-configured Pulumi CLI program that
        ///     already exists on disk, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static Task<WorkspaceStack> CreateStackAsync(LocalProgramArgs args, CancellationToken cancellationToken)
            => CreateStackHelperAsync(args, WorkspaceStack.CreateAsync, cancellationToken);

        /// <summary>
        /// Selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the specified
        /// inline (in process) <see cref="LocalWorkspaceOptions.Program"/>. This program
        /// is fully debuggable and runs in process. If no <see cref="LocalWorkspaceOptions.ProjectSettings"/>
        /// option is specified, default project settings will be created on behalf of the user. Similarly, unless a
        /// <see cref="LocalWorkspaceOptions.WorkDir"/> option is specified, the working directory will default
        /// to a new temporary directory provided by the OS.
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with an inline <see cref="PulumiFn"/> program
        ///     that runs in process, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        public static Task<WorkspaceStack> SelectStackAsync(InlineProgramArgs args)
            => SelectStackAsync(args, default);

        /// <summary>
        /// Selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the specified
        /// inline (in process) <see cref="LocalWorkspaceOptions.Program"/>. This program
        /// is fully debuggable and runs in process. If no <see cref="LocalWorkspaceOptions.ProjectSettings"/>
        /// option is specified, default project settings will be created on behalf of the user. Similarly, unless a
        /// <see cref="LocalWorkspaceOptions.WorkDir"/> option is specified, the working directory will default
        /// to a new temporary directory provided by the OS.
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with an inline <see cref="PulumiFn"/> program
        ///     that runs in process, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static Task<WorkspaceStack> SelectStackAsync(InlineProgramArgs args, CancellationToken cancellationToken)
            => CreateStackHelperAsync(args, WorkspaceStack.SelectAsync, cancellationToken);

        /// <summary>
        /// Selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the local Pulumi CLI program
        /// from the specified <see cref="LocalWorkspaceOptions.WorkDir"/>. This is a way to create drivers
        /// on top of pre-existing Pulumi programs. This Workspace will pick up any available Settings
        /// files(Pulumi.yaml, Pulumi.{stack}.yaml).
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with a pre-configured Pulumi CLI program that
        ///     already exists on disk, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        public static Task<WorkspaceStack> SelectStackAsync(LocalProgramArgs args)
            => SelectStackAsync(args, default);

        /// <summary>
        /// Selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the local Pulumi CLI program
        /// from the specified <see cref="LocalWorkspaceOptions.WorkDir"/>. This is a way to create drivers
        /// on top of pre-existing Pulumi programs. This Workspace will pick up any available Settings
        /// files(Pulumi.yaml, Pulumi.{stack}.yaml).
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with a pre-configured Pulumi CLI program that
        ///     already exists on disk, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static Task<WorkspaceStack> SelectStackAsync(LocalProgramArgs args, CancellationToken cancellationToken)
            => CreateStackHelperAsync(args, WorkspaceStack.SelectAsync, cancellationToken);

        /// <summary>
        /// Creates or selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the specified
        /// inline (in process) <see cref="LocalWorkspaceOptions.Program"/>. This program
        /// is fully debuggable and runs in process. If no <see cref="LocalWorkspaceOptions.ProjectSettings"/>
        /// option is specified, default project settings will be created on behalf of the user. Similarly, unless a
        /// <see cref="LocalWorkspaceOptions.WorkDir"/> option is specified, the working directory will default
        /// to a new temporary directory provided by the OS.
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with an inline <see cref="PulumiFn"/> program
        ///     that runs in process, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        public static Task<WorkspaceStack> CreateOrSelectStackAsync(InlineProgramArgs args)
            => CreateOrSelectStackAsync(args, default);

        /// <summary>
        /// Creates or selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the specified
        /// inline (in process) <see cref="LocalWorkspaceOptions.Program"/>. This program
        /// is fully debuggable and runs in process. If no <see cref="LocalWorkspaceOptions.ProjectSettings"/>
        /// option is specified, default project settings will be created on behalf of the user. Similarly, unless a
        /// <see cref="LocalWorkspaceOptions.WorkDir"/> option is specified, the working directory will default
        /// to a new temporary directory provided by the OS.
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with an inline <see cref="PulumiFn"/> program
        ///     that runs in process, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static Task<WorkspaceStack> CreateOrSelectStackAsync(InlineProgramArgs args, CancellationToken cancellationToken)
            => CreateStackHelperAsync(args, WorkspaceStack.CreateOrSelectAsync, cancellationToken);

        /// <summary>
        /// Creates or selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the local Pulumi CLI program
        /// from the specified <see cref="LocalWorkspaceOptions.WorkDir"/>. This is a way to create drivers
        /// on top of pre-existing Pulumi programs. This Workspace will pick up any available Settings
        /// files(Pulumi.yaml, Pulumi.{stack}.yaml).
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with a pre-configured Pulumi CLI program that
        ///     already exists on disk, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        public static Task<WorkspaceStack> CreateOrSelectStackAsync(LocalProgramArgs args)
            => CreateOrSelectStackAsync(args, default);

        /// <summary>
        /// Creates or selects an existing Stack with a <see cref="LocalWorkspace"/> utilizing the local Pulumi CLI program
        /// from the specified <see cref="LocalWorkspaceOptions.WorkDir"/>. This is a way to create drivers
        /// on top of pre-existing Pulumi programs. This Workspace will pick up any available Settings
        /// files(Pulumi.yaml, Pulumi.{stack}.yaml).
        /// </summary>
        /// <param name="args">
        ///     A set of arguments to initialize a Stack with a pre-configured Pulumi CLI program that
        ///     already exists on disk, as well as any additional customizations to be applied to the
        ///     workspace.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static Task<WorkspaceStack> CreateOrSelectStackAsync(LocalProgramArgs args, CancellationToken cancellationToken)
            => CreateStackHelperAsync(args, WorkspaceStack.CreateOrSelectAsync, cancellationToken);

        private static async Task<WorkspaceStack> CreateStackHelperAsync(
            InlineProgramArgs args,
            Func<string, Workspace, CancellationToken, Task<WorkspaceStack>> initFunc,
            CancellationToken cancellationToken)
        {
            if (args.ProjectSettings is null)
                throw new ArgumentNullException(nameof(args.ProjectSettings));

            var ws = new LocalWorkspace(
                new LocalPulumiCmd(),
                args,
                cancellationToken);
            await ws._readyTask.ConfigureAwait(false);

            return await initFunc(args.StackName, ws, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<WorkspaceStack> CreateStackHelperAsync(
            LocalProgramArgs args,
            Func<string, Workspace, CancellationToken, Task<WorkspaceStack>> initFunc,
            CancellationToken cancellationToken)
        {
            var ws = new LocalWorkspace(
                new LocalPulumiCmd(),
                args,
                cancellationToken);
            await ws._readyTask.ConfigureAwait(false);

            return await initFunc(args.StackName, ws, cancellationToken).ConfigureAwait(false);
        }

        internal LocalWorkspace(
            IPulumiCmd cmd,
            LocalWorkspaceOptions? options,
            CancellationToken cancellationToken)
            : base(cmd)
        {
            string? dir = null;
            var readyTasks = new List<Task>();

            if (options != null)
            {
                if (!string.IsNullOrWhiteSpace(options.WorkDir))
                    dir = options.WorkDir;

                this.PulumiHome = options.PulumiHome;
                this.Program = options.Program;
                this.SecretsProvider = options.SecretsProvider;

                if (options.EnvironmentVariables != null)
                    this.EnvironmentVariables = new Dictionary<string, string>(options.EnvironmentVariables);
            }

            if (string.IsNullOrWhiteSpace(dir))
            {
                // note that csharp doesn't guarantee that Path.GetRandomFileName returns a name
                // for a file or folder that doesn't already exist.
                // we should be OK with the "automation-" prefix but a collision is still
                // theoretically possible
                dir = Path.Combine(Path.GetTempPath(), $"automation-{Path.GetRandomFileName()}");
                Directory.CreateDirectory(dir);
                this._ownsWorkingDir = true;
            }

            this.WorkDir = dir;

            // these are after working dir is set because they start immediately
            if (options?.ProjectSettings != null)
                readyTasks.Add(this.SaveProjectSettingsAsync(options.ProjectSettings, cancellationToken));

            if (options?.StackSettings != null && options.StackSettings.Any())
            {
                foreach (var pair in options.StackSettings)
                    readyTasks.Add(this.SaveStackSettingsAsync(pair.Key, pair.Value, cancellationToken));
            }

            this._readyTask = Task.WhenAll(readyTasks);
        }

        private static readonly string[] SettingsExtensions = new string[] { ".yaml", ".yml", ".json" };

        /// <inheritdoc/>
        public override async Task<ProjectSettings?> GetProjectSettingsAsync(CancellationToken cancellationToken = default)
        {
            foreach (var ext in SettingsExtensions)
            {
                var isJson = ext == ".json";
                var path = Path.Combine(this.WorkDir, $"Pulumi{ext}");
                if (!File.Exists(path))
                    continue;

                var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (isJson)
                    return this._serializer.DeserializeJson<ProjectSettings>(content);

                var model = this._serializer.DeserializeYaml<ProjectSettingsModel>(content);
                return model.Convert();
            }

            return null;
        }

        /// <inheritdoc/>
        public override Task SaveProjectSettingsAsync(ProjectSettings settings, CancellationToken cancellationToken = default)
        {
            var foundExt = ".yaml";
            foreach (var ext in SettingsExtensions)
            {
                var testPath = Path.Combine(this.WorkDir, $"Pulumi{ext}");
                if (File.Exists(testPath))
                {
                    foundExt = ext;
                    break;
                }
            }

            var path = Path.Combine(this.WorkDir, $"Pulumi{foundExt}");
            var content = foundExt == ".json" ? this._serializer.SerializeJson(settings) : this._serializer.SerializeYaml(settings);
            return File.WriteAllTextAsync(path, content, cancellationToken);
        }

        private static string GetStackSettingsName(string stackName)
        {
            var parts = stackName.Split('/');
            if (parts.Length < 1)
                return stackName;

            return parts[^1];
        }

        /// <inheritdoc/>
        public override async Task<StackSettings?> GetStackSettingsAsync(string stackName, CancellationToken cancellationToken = default)
        {
            var settingsName = GetStackSettingsName(stackName);

            foreach (var ext in SettingsExtensions)
            {
                var isJson = ext == ".json";
                var path = Path.Combine(this.WorkDir, $"Pulumi.{settingsName}{ext}");
                if (!File.Exists(path))
                    continue;

                var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                return isJson ? this._serializer.DeserializeJson<StackSettings>(content) : this._serializer.DeserializeYaml<StackSettings>(content);
            }

            return null;
        }

        /// <inheritdoc/>
        public override Task SaveStackSettingsAsync(string stackName, StackSettings settings, CancellationToken cancellationToken = default)
        {
            var settingsName = GetStackSettingsName(stackName);

            var foundExt = ".yaml";
            foreach (var ext in SettingsExtensions)
            {
                var testPath = Path.Combine(this.WorkDir, $"Pulumi.{settingsName}{ext}");
                if (File.Exists(testPath))
                {
                    foundExt = ext;
                    break;
                }
            }

            var path = Path.Combine(this.WorkDir, $"Pulumi.{settingsName}{foundExt}");
            var content = foundExt == ".json" ? this._serializer.SerializeJson(settings) : this._serializer.SerializeYaml(settings);
            return File.WriteAllTextAsync(path, content, cancellationToken);
        }

        /// <inheritdoc/>
        public override Task<ImmutableList<string>> SerializeArgsForOpAsync(string stackName, CancellationToken cancellationToken = default)
            => Task.FromResult(ImmutableList<string>.Empty);

        /// <inheritdoc/>
        public override Task PostCommandCallbackAsync(string stackName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <inheritdoc/>
        public override async Task<ConfigValue> GetConfigValueAsync(string stackName, string key, CancellationToken cancellationToken = default)
        {
            await this.SelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);
            var result = await this.RunCommandAsync(new[] { "config", "get", key, "--json" }, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ConfigValue>(result.StandardOutput);
        }

        /// <inheritdoc/>
        public override async Task<ImmutableDictionary<string, ConfigValue>> GetConfigAsync(string stackName, CancellationToken cancellationToken = default)
        {
            await this.SelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);
            return await this.GetConfigAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<ImmutableDictionary<string, ConfigValue>> GetConfigAsync(CancellationToken cancellationToken)
        {
            var result = await this.RunCommandAsync(new[] { "config", "--show-secrets", "--json" }, cancellationToken).ConfigureAwait(false);
            var dict = this._serializer.DeserializeJson<Dictionary<string, ConfigValue>>(result.StandardOutput);
            return dict.ToImmutableDictionary();
        }

        /// <inheritdoc/>
        public override async Task SetConfigValueAsync(string stackName, string key, ConfigValue value, CancellationToken cancellationToken = default)
        {
            await this.SelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);
            await this.SetConfigValueAsync(key, value, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task SetConfigAsync(string stackName, IDictionary<string, ConfigValue> configMap, CancellationToken cancellationToken = default)
        {
            // TODO: do this in parallel after this is fixed https://github.com/pulumi/pulumi/issues/3877
            await this.SelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);

            foreach (var (key, value) in configMap)
                await this.SetConfigValueAsync(key, value, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetConfigValueAsync(string key, ConfigValue value, CancellationToken cancellationToken)
        {
            var secretArg = value.IsSecret ? "--secret" : "--plaintext";
            await this.RunCommandAsync(new[] { "config", "set", key, value.Value, secretArg }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task RemoveConfigValueAsync(string stackName, string key, CancellationToken cancellationToken = default)
        {
            await this.SelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);
            await this.RunCommandAsync(new[] { "config", "rm", key }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task RemoveConfigAsync(string stackName, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            // TODO: do this in parallel after this is fixed https://github.com/pulumi/pulumi/issues/3877
            await this.SelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);

            foreach (var key in keys)
                await this.RunCommandAsync(new[] { "config", "rm", key }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<ImmutableDictionary<string, ConfigValue>> RefreshConfigAsync(string stackName, CancellationToken cancellationToken = default)
        {
            await this.SelectStackAsync(stackName, cancellationToken).ConfigureAwait(false);
            await this.RunCommandAsync(new[] { "config", "refresh", "--force" }, cancellationToken).ConfigureAwait(false);
            return await this.GetConfigAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async Task<WhoAmIResult> WhoAmIAsync(CancellationToken cancellationToken = default)
        {
            var result = await this.RunCommandAsync(new[] { "whoami" }, cancellationToken).ConfigureAwait(false);
            return new WhoAmIResult(result.StandardOutput.Trim());
        }

        /// <inheritdoc/>
        public override Task CreateStackAsync(string stackName, CancellationToken cancellationToken)
        {
            var args = new List<string>()
            {
                "stack",
                "init",
                stackName,
            };

            if (!string.IsNullOrWhiteSpace(this.SecretsProvider))
                args.AddRange(new[] { "--secrets-provider", this.SecretsProvider });

            return this.RunCommandAsync(args, cancellationToken);
        }

        /// <inheritdoc/>
        public override Task SelectStackAsync(string stackName, CancellationToken cancellationToken)
            => this.RunCommandAsync(new[] { "stack", "select", stackName }, cancellationToken);

        /// <inheritdoc/>
        public override Task RemoveStackAsync(string stackName, CancellationToken cancellationToken = default)
            => this.RunCommandAsync(new[] { "stack", "rm", "--yes", stackName }, cancellationToken);

        /// <inheritdoc/>
        public override async Task<ImmutableList<StackSummary>> ListStacksAsync(CancellationToken cancellationToken = default)
        {
            var result = await this.RunCommandAsync(new[] { "stack", "ls", "--json" }, cancellationToken).ConfigureAwait(false);
            var stacks = this._serializer.DeserializeJson<List<StackSummary>>(result.StandardOutput);
            return stacks.ToImmutableList();
        }

        /// <inheritdoc/>
        public override Task InstallPluginAsync(string name, string version, PluginKind kind = PluginKind.Resource, CancellationToken cancellationToken = default)
            => this.RunCommandAsync(new[] { "plugin", "install", kind.ToString().ToLower(), name, version }, cancellationToken);

        /// <inheritdoc/>
        public override Task RemovePluginAsync(string? name = null, string? versionRange = null, PluginKind kind = PluginKind.Resource, CancellationToken cancellationToken = default)
        {
            var args = new List<string>()
            {
                "plugin",
                "rm",
                kind.ToString().ToLower(),
            };

            if (!string.IsNullOrWhiteSpace(name))
                args.Add(name);

            if (!string.IsNullOrWhiteSpace(versionRange))
                args.Add(versionRange);

            args.Add("--yes");
            return this.RunCommandAsync(args, cancellationToken);
        }

        /// <inheritdoc/>
        public override async Task<ImmutableList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken = default)
        {
            var result = await this.RunCommandAsync(new[] { "plugin", "ls", "--json" }, cancellationToken).ConfigureAwait(false);
            var plugins = this._serializer.DeserializeJson<List<PluginInfo>>(result.StandardOutput);
            return plugins.ToImmutableList();
        }

        public override void Dispose()
        {
            base.Dispose();

            if (this._ownsWorkingDir
                && !string.IsNullOrWhiteSpace(this.WorkDir)
                && Directory.Exists(this.WorkDir))
            {
                try
                {
                    Directory.Delete(this.WorkDir, true);
                }
                catch
                {
                    // allow graceful exit if for some reason
                    // we're not able to delete the directory
                    // will rely on OS to clean temp directory
                    // in this case.
                }
            }
        }
    }
}
