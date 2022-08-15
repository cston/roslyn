﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Composition;
using Microsoft.CodeAnalysis.BraceMatching;
using Microsoft.CodeAnalysis.CSharp.EmbeddedLanguages.LanguageServices;
using Microsoft.CodeAnalysis.CSharp.LanguageService;
using Microsoft.CodeAnalysis.EmbeddedLanguages;
using Microsoft.CodeAnalysis.Host.Mef;

namespace Microsoft.CodeAnalysis.CSharp.BraceMatching
{
    [ExportBraceMatcher(LanguageNames.CSharp), Shared]
    internal class CSharpEmbeddedLanguageBraceMatcher : AbstractEmbeddedLanguageBraceMatcher
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public CSharpEmbeddedLanguageBraceMatcher(
            [ImportMany] IEnumerable<Lazy<IEmbeddedLanguageBraceMatcher, EmbeddedLanguageMetadata>> services)
            : base(LanguageNames.CSharp, CSharpEmbeddedLanguagesProvider.Info, CSharpSyntaxKinds.Instance, services)
        {
        }
    }
}
