// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.Emit;
using System.Text;
using System.Diagnostics;

namespace Microsoft.Cci
{
    internal static class TypeNameSerializer
    {
        internal static string GetSerializedTypeName(this ITypeReference typeReference, EmitContext context)
        {
            return GetSerializedTypeName(typeReference, context, assemblyQualify: true).Name;
        }

        internal static (string Name, bool isQualified) GetSerializedTypeName(this ITypeReference typeReference, EmitContext context, bool assemblyQualify)
        {
            var pooled = PooledStringBuilder.GetInstance();
            StringBuilder sb = pooled.Builder;
            IArrayTypeReference arrType = typeReference as IArrayTypeReference;
            if (arrType != null)
            {
                typeReference = arrType.GetElementType(context);
                sb.Append(GetSerializedTypeName(typeReference, context, assemblyQualify: false).Name);
                if (arrType.IsSZArray)
                {
                    sb.Append("[]");
                }
                else
                {
                    sb.Append('[');
                    if (arrType.Rank == 1)
                    {
                        sb.Append('*');
                    }

                    sb.Append(',', arrType.Rank - 1);

                    sb.Append(']');
                }

                goto done;
            }

            IPointerTypeReference pointer = typeReference as IPointerTypeReference;
            if (pointer != null)
            {
                typeReference = pointer.GetTargetType(context);
                sb.Append(GetSerializedTypeName(typeReference, context, assemblyQualify: false).Name);
                sb.Append('*');
                goto done;
            }

            INamespaceTypeReference namespaceType = typeReference.AsNamespaceTypeReference;
            if (namespaceType != null)
            {
                var name = namespaceType.NamespaceName;
                if (name.Length != 0)
                {
                    sb.Append(name);
                    sb.Append('.');
                }

                sb.Append(GetMangledAndEscapedName(namespaceType));
                goto done;
            }


            if (typeReference.IsTypeSpecification())
            {
                ITypeReference uninstantiatedTypeReference = typeReference.GetUninstantiatedGenericType(context);

                ArrayBuilder<ITypeReference> consolidatedTypeArguments = ArrayBuilder<ITypeReference>.GetInstance();
                typeReference.GetConsolidatedTypeArguments(consolidatedTypeArguments, context);

                sb.Append(GetSerializedTypeName(uninstantiatedTypeReference, context, assemblyQualify: false).Name);
                sb.Append('[');
                bool first = true;
                foreach (ITypeReference argument in consolidatedTypeArguments)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        sb.Append(',');
                    }

                    var (argTypeName, isQualified) = GetSerializedTypeName(argument, context, assemblyQualify: true);
                    if (isQualified)
                    {
                        sb.Append('[');
                    }

                    sb.Append(argTypeName);
                    if (isQualified)
                    {
                        sb.Append(']');
                    }
                }
                consolidatedTypeArguments.Free();

                sb.Append(']');
                goto done;
            }

            INestedTypeReference nestedType = typeReference.AsNestedTypeReference;
            if (nestedType != null)
            {
                sb.Append(GetSerializedTypeName(nestedType.GetContainingType(context), context, assemblyQualify: false).Name);
                sb.Append('+');
                sb.Append(GetMangledAndEscapedName(nestedType));
                goto done;
            }

            // TODO: error
            done:
            bool isAssemblyQualified;
            if (assemblyQualify)
            {
                AppendAssemblyQualifierIfNecessary(sb, UnwrapTypeReference(typeReference, context), out isAssemblyQualified, context);
            }
            else
            {
                isAssemblyQualified = false;
            }

            return (pooled.ToStringAndFree(), isAssemblyQualified);
        }

        private static void AppendAssemblyQualifierIfNecessary(StringBuilder sb, ITypeReference typeReference, out bool isAssemQualified, EmitContext context)
        {
            INestedTypeReference nestedType = typeReference.AsNestedTypeReference;
            if (nestedType != null)
            {
                AppendAssemblyQualifierIfNecessary(sb, nestedType.GetContainingType(context), out isAssemQualified, context);
                return;
            }

            IGenericTypeInstanceReference genInst = typeReference.AsGenericTypeInstanceReference;
            if (genInst != null)
            {
                AppendAssemblyQualifierIfNecessary(sb, genInst.GetGenericType(context), out isAssemQualified, context);
                return;
            }

            IArrayTypeReference arrType = typeReference as IArrayTypeReference;
            if (arrType != null)
            {
                AppendAssemblyQualifierIfNecessary(sb, arrType.GetElementType(context), out isAssemQualified, context);
                return;
            }

            IPointerTypeReference pointer = typeReference as IPointerTypeReference;
            if (pointer != null)
            {
                AppendAssemblyQualifierIfNecessary(sb, pointer.GetTargetType(context), out isAssemQualified, context);
                return;
            }

            isAssemQualified = false;
            IAssemblyReference referencedAssembly = null;
            INamespaceTypeReference namespaceType = typeReference.AsNamespaceTypeReference;
            if (namespaceType != null)
            {
                referencedAssembly = namespaceType.GetUnit(context) as IAssemblyReference;
            }

            if (referencedAssembly != null)
            {
                var containingAssembly = context.Module.GetContainingAssembly(context);

                if (containingAssembly == null || !ReferenceEquals(referencedAssembly, containingAssembly))
                {
                    sb.Append(", ");
                    sb.Append(MetadataWriter.StrongName(referencedAssembly));
                    isAssemQualified = true;
                }
            }
        }

        private static string GetMangledAndEscapedName(INamedTypeReference namedType)
        {
            var pooled = PooledStringBuilder.GetInstance();
            StringBuilder mangledName = pooled.Builder;

            const string needsEscaping = "\\[]*.+,& ";
            foreach (var ch in namedType.Name)
            {
                if (needsEscaping.IndexOf(ch) >= 0)
                {
                    mangledName.Append('\\');
                }

                mangledName.Append(ch);
            }

            if (namedType.MangleName && namedType.GenericParameterCount > 0)
            {
                mangledName.Append(MetadataHelpers.GetAritySuffix(namedType.GenericParameterCount));
            }

            return pooled.ToStringAndFree();
        }

        /// <summary>
        /// Strip off *, &amp;, and [].
        /// </summary>
        private static ITypeReference UnwrapTypeReference(ITypeReference typeReference, EmitContext context)
        {
            while (true)
            {
                IArrayTypeReference arrType = typeReference as IArrayTypeReference;
                if (arrType != null)
                {
                    typeReference = arrType.GetElementType(context);
                    continue;
                }

                IPointerTypeReference pointer = typeReference as IPointerTypeReference;
                if (pointer != null)
                {
                    typeReference = pointer.GetTargetType(context);
                    continue;
                }

                return typeReference;
            }
        }

        /// <summary>
        /// Qualified name of namespace.
        /// e.g. "A.B.C"
        /// </summary>
        internal static string BuildQualifiedNamespaceName(INamespace @namespace)
        {
            Debug.Assert(@namespace != null);

            if (@namespace.ContainingNamespace == null)
            {
                return @namespace.Name;
            }

            var namesReversed = ArrayBuilder<string>.GetInstance();
            do
            {
                string name = @namespace.Name;
                if (name.Length != 0)
                {
                    namesReversed.Add(name);
                }

                @namespace = @namespace.ContainingNamespace;
            }
            while (@namespace != null);

            var result = PooledStringBuilder.GetInstance();

            for (int i = namesReversed.Count - 1; i >= 0; i--)
            {
                result.Builder.Append(namesReversed[i]);

                if (i > 0)
                {
                    result.Builder.Append('.');
                }
            }

            namesReversed.Free();
            return result.ToStringAndFree();
        }
    }
}