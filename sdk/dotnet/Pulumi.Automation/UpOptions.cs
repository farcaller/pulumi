﻿// Copyright 2016-2021, Pulumi Corporation

using System;
using System.Collections.Generic;

namespace Pulumi.Automation
{
    /// <summary>
    /// Options controlling the behavior of an <see cref="WorkspaceStack.UpAsync(UpOptions, System.Threading.CancellationToken)"/> operation.
    /// </summary>
    public sealed class UpOptions : UpdateOptions
    {
        public bool? ExpectNoChanges { get; set; }

        public List<string>? Replace { get; set; }

        public bool? TargetDependents { get; set; }

        public Action<string>? OnOutput { get; set; }

        public PulumiFn? Program { get; set; }
    }
}
