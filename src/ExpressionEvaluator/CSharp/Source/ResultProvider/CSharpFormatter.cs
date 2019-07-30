// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.CodeAnalysis.ExpressionEvaluator;
using Microsoft.CodeAnalysis.PooledObjects;
using Type = Microsoft.VisualStudio.Debugger.Metadata.Type;

namespace Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator
{
    internal sealed partial class CSharpFormatter : Formatter
    {
        public CSharpFormatter()
            : base(defaultFormat: "{{{0}}}", nullString: "null", thisString: "this")
        {
        }

        internal override bool IsValidIdentifier(string name)
        {
            return SyntaxFacts.IsValidIdentifier(name);
        }

        internal override bool IsIdentifierPartCharacter(char c)
        {
            return SyntaxFacts.IsIdentifierPartCharacter(c);
        }

        internal override bool IsPredefinedType(Type type)
        {
            return type.IsPredefinedType();
        }

        internal override bool IsWhitespace(char c)
        {
            return SyntaxFacts.IsWhitespace(c);
        }
    }
}
