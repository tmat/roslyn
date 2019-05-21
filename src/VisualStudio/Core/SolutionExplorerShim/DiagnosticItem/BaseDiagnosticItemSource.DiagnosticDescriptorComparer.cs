// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.SolutionExplorer
{
    internal abstract partial class BaseDiagnosticItemSource
    {
        internal sealed class DiagnosticDescriptorComparer : IComparer<DiagnosticDescriptor>
        {
            private readonly CultureInfo _culture;

            public DiagnosticDescriptorComparer(CultureInfo culture)
            {
                Contract.ThrowIfNull(culture);
                _culture = culture;
            }

            public int Compare(DiagnosticDescriptor x, DiagnosticDescriptor y)
            {
                var comparison = StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = _culture.CompareInfo.Compare(x.Title.ToString(_culture), y.Title.ToString(_culture));
                if (comparison != 0)
                {
                    return comparison;
                }

                return _culture.CompareInfo.Compare(x.MessageFormat.ToString(_culture), y.MessageFormat.ToString(_culture));
            }
        }
    }
}
