// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Evaluation.ClrCompilation;

namespace Microsoft.CodeAnalysis.ExpressionEvaluator
{
    /// <summary>
    /// A pair of DkmClrValue and Expansion, used to store
    /// state on a DkmEvaluationResult.  Also computes the
    /// full name of the DkmClrValue.
    /// </summary>
    /// <remarks>
    /// The DkmClrValue is included here rather than directly
    /// on the Expansion so that the DkmClrValue is not kept
    /// alive by the Expansion.
    /// </remarks>
    internal sealed class EvalResultDataItem : DkmDataItem
    {
        public readonly string Name;
        public readonly TypeAndCustomInfo DeclaredTypeAndInfo;
        public readonly DkmClrValue Value;
        public readonly Expansion Expansion;
        public readonly DkmFullNameDescriptor FullName;
        public readonly ReadOnlyCollection<string> FormatSpecifiers;
        public readonly DkmFullNameDescriptor ChildFullNamePrefix;

        public EvalResultDataItem(
            string name,
            TypeAndCustomInfo declaredTypeAndInfo,
            DkmClrValue value,
            Expansion expansion,
            DkmFullNameDescriptor fullName,
            DkmFullNameDescriptor childFullNamePrefixOpt,
            ReadOnlyCollection<string> formatSpecifiers)
        {
            Name = name;
            DeclaredTypeAndInfo = declaredTypeAndInfo;
            Value = value;
            FullName = fullName;
            ChildFullNamePrefix = childFullNamePrefixOpt;
            FormatSpecifiers = formatSpecifiers;
            Expansion = expansion;
        }

        protected override void OnClose()
        {
            // If we have an expansion, there's a danger that more than one data item is 
            // referring to the same DkmClrValue (e.g. if it's an AggregateExpansion).
            // To be safe, we'll only call Close when there's no expansion.  Since this
            // is only an optimization (the debugger will eventually close the value
            // anyway), a conservative approach is acceptable.
            if (this.Expansion == null)
            {
                Value.Close();
            }
        }
    }

    internal enum ExpansionKind
    {
        Default,
        Explicit, // All interesting fields set explicitly including DisplayName, DisplayValue, DisplayType.
        DynamicView,
        Error,
        NativeView,
        NonPublicMembers,
        PointerDereference,
        RawView,
        ResultsView,
        StaticMembers,
        TypeVariable
    }

    internal sealed class EvalResult
    {
        public readonly ExpansionKind Kind;
        public readonly string Name;
        public readonly TypeAndCustomInfo TypeDeclaringMemberAndInfo;
        public readonly TypeAndCustomInfo DeclaredTypeAndInfo;
        public readonly bool UseDebuggerDisplay;
        public readonly DkmClrValue Value;
        public readonly string DisplayName;
        public readonly string DisplayValue; // overrides the "Value" text displayed for certain kinds of DataItems (errors, invalid pointer dereferences, etc)...not to be confused with DebuggerDisplayAttribute Value...
        public readonly string DisplayType;
        public readonly Expansion Expansion;
        public readonly DkmFullNameDescriptor FullName;
        public readonly ReadOnlyCollection<string> FormatSpecifiers;
        public readonly DkmFullNameDescriptor ChildFullNamePrefix;
        public readonly DkmEvaluationResultCategory Category;
        public readonly DkmEvaluationResultFlags Flags;
        public readonly string EditableValue;
        public readonly DkmInspectionContext InspectionContext;

        public EvalResult(string name, string errorMessage, DkmInspectionContext inspectionContext)
            : this(
                ExpansionKind.Error,
                name: name,
                typeDeclaringMemberAndInfo: default,
                declaredTypeAndInfo: default,
                useDebuggerDisplay: false,
                value: null,
                displayValue: errorMessage,
                expansion: null,
                fullName: default,
                childFullNamePrefixOpt: default,
                formatSpecifiers: Formatter.NoFormatSpecifiers,
                category: DkmEvaluationResultCategory.Other,
                flags: DkmEvaluationResultFlags.None,
                editableValue: null,
                inspectionContext: inspectionContext)
        {
        }

        public EvalResult(
            ExpansionKind kind,
            string name,
            TypeAndCustomInfo typeDeclaringMemberAndInfo,
            TypeAndCustomInfo declaredTypeAndInfo,
            bool useDebuggerDisplay,
            DkmClrValue value,
            string displayValue,
            Expansion expansion,
            DkmFullNameDescriptor fullName,
            DkmFullNameDescriptor childFullNamePrefixOpt,
            ReadOnlyCollection<string> formatSpecifiers,
            DkmEvaluationResultCategory category,
            DkmEvaluationResultFlags flags,
            string editableValue,
            DkmInspectionContext inspectionContext,
            string displayName = null,
            string displayType = null)
        {
            Debug.Assert(name != null);
            Debug.Assert(formatSpecifiers != null);
            Debug.Assert((flags & DkmEvaluationResultFlags.Expandable) == 0);

            Kind = kind;
            Name = name;
            TypeDeclaringMemberAndInfo = typeDeclaringMemberAndInfo;
            DeclaredTypeAndInfo = declaredTypeAndInfo;
            UseDebuggerDisplay = useDebuggerDisplay;
            Value = value;
            DisplayValue = displayValue;
            FullName = fullName;
            ChildFullNamePrefix = childFullNamePrefixOpt;
            FormatSpecifiers = formatSpecifiers;
            Category = category;
            EditableValue = editableValue;
            Flags = flags | GetFlags(value, inspectionContext) | ((expansion == null) ? DkmEvaluationResultFlags.None : DkmEvaluationResultFlags.Expandable);
            Expansion = expansion;
            InspectionContext = inspectionContext;
            DisplayName = displayName;
            DisplayType = displayType;
        }

        internal EvalResultDataItem ToDataItem()
        {
            return new EvalResultDataItem(
                Name,
                DeclaredTypeAndInfo,
                Value,
                Expansion,
                FullName,
                ChildFullNamePrefix,
                FormatSpecifiers);
        }

        private static DkmEvaluationResultFlags GetFlags(DkmClrValue value, DkmInspectionContext inspectionContext)
        {
            if (value == null)
            {
                return DkmEvaluationResultFlags.None;
            }

            var resultFlags = value.EvalFlags;
            var type = value.Type.GetLmrType();

            if (type.IsBoolean())
            {
                resultFlags |= DkmEvaluationResultFlags.Boolean;
                if (true.Equals(value.HostObjectValue))
                {
                    resultFlags |= DkmEvaluationResultFlags.BooleanTrue;
                }
            }

            if (!value.IsError() && value.HasUnderlyingString(inspectionContext))
            {
                resultFlags |= DkmEvaluationResultFlags.RawString;
            }

            // As in the old EE, we won't allow editing members of a DynamicView expansion.
            if (type.IsDynamicProperty())
            {
                resultFlags |= DkmEvaluationResultFlags.ReadOnly;
            }

            return resultFlags;
        }
    }
}
