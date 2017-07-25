using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.ExpressionEvaluator
{
    internal struct Alias
    {
        internal Alias(ClrAliasKind kind, string name, string fullName, string type, Guid customTypeInfoId, ReadOnlyCollection<byte> customTypeInfo)
        {
            Debug.Assert(!string.IsNullOrEmpty(fullName));
            Debug.Assert(!string.IsNullOrEmpty(type));

            this.Kind = kind;
            this.Name = name;
            this.FullName = fullName;
            this.Type = type;
            this.CustomTypeInfoId = customTypeInfoId;
            this.CustomTypeInfo = customTypeInfo;
        }

        internal readonly ClrAliasKind Kind;
        internal readonly string Name;
        internal readonly string FullName;
        internal readonly string Type;
        internal readonly Guid CustomTypeInfoId;
        internal readonly ReadOnlyCollection<byte> CustomTypeInfo;
    }
}
