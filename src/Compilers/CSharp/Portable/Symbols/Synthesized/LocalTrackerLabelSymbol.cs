// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.Symbols;

namespace Microsoft.CodeAnalysis.CSharp.Symbols;

internal sealed class LocalTrackerLabelSymbol : LabelSymbol, ILocalStoreTrackerLabel
{
    public LocalSymbol Context { get; }
    public MethodSymbol GenericLogger { get; }
    public ImmutableHashSet<LocalSymbol> UserLocalsWithWritableAddress { get; }
    
    public ImmutableDictionary<ILocalSymbolInternal, uint>? LoggerTokens { get; set; }

    public LocalTrackerLabelSymbol(LocalSymbol context, MethodSymbol genericLogger, ImmutableHashSet<LocalSymbol> userLocalsWithWritableAddress)
    {
        Context = context;
        GenericLogger = genericLogger;
        UserLocalsWithWritableAddress = userLocalsWithWritableAddress;
    }

    public override string Name
        => "localTrackerLabel";

    public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        => ImmutableArray<SyntaxReference>.Empty;

    public override bool IsImplicitlyDeclared
        => true;

    public void Emit(LocalSlotManager localSlotManager, BlobBuilder builder)
    {
        Debug.Assert(LoggerTokens != null);

        // variable has been lifted:
        if (!localSlotManager.TryGetLocal(Context, out var contextLocal))
        {
            return;
        }

        var contextLocalSlot = contextLocal.SlotIndex;
        Debug.Assert(contextLocalSlot >= 0);

        foreach (var localDef in localSlotManager.GetAllLocalDefinitions())
        {
            var slot = localDef.SlotIndex;

            if (slot >= 0 && localDef.SymbolOpt != null && LoggerTokens.TryGetValue(localDef.SymbolOpt, out var loggerToken))
            {
                // ldloca context 
                Debug.Assert(Context.Type.IsStructType());
                EmitLoadLocalAddress(builder, contextLocalSlot);

                // ldloca local
                EmitLoadLocalAddress(builder, slot);

                // ldc index
                Debug.Assert(slot <= ushort.MaxValue);
                EmitIntConstant(builder, (ushort)slot);

                // call
                EmitOpCode(builder, ILOpCode.Call);
                builder.WriteUInt32(loggerToken);
            }
        }
    }

    private static void EmitLoadLocalAddress(BlobBuilder builder, int slot)
    {
        if (slot < 0xFF)
        {
            EmitOpCode(builder, ILOpCode.Ldloca_s);
            builder.WriteByte((byte)slot);
        }
        else
        {
            EmitOpCode(builder, ILOpCode.Ldloca);
            builder.WriteInt32(slot);
        }
    }

    private static void EmitOpCode(BlobBuilder builder, ILOpCode code)
    {
        if (code.Size() == 1)
        {
            builder.WriteByte((byte)code);
        }
        else
        {
            builder.WriteByte((byte)((ushort)code >> 8));
            builder.WriteByte((byte)((ushort)code & 0xff));
        }
    }

    private static void EmitIntConstant(BlobBuilder builder, int value)
    {
        ILOpCode code = ILOpCode.Nop;
        switch (value)
        {
            case -1: code = ILOpCode.Ldc_i4_m1; break;
            case 0: code = ILOpCode.Ldc_i4_0; break;
            case 1: code = ILOpCode.Ldc_i4_1; break;
            case 2: code = ILOpCode.Ldc_i4_2; break;
            case 3: code = ILOpCode.Ldc_i4_3; break;
            case 4: code = ILOpCode.Ldc_i4_4; break;
            case 5: code = ILOpCode.Ldc_i4_5; break;
            case 6: code = ILOpCode.Ldc_i4_6; break;
            case 7: code = ILOpCode.Ldc_i4_7; break;
            case 8: code = ILOpCode.Ldc_i4_8; break;
        }

        if (code != ILOpCode.Nop)
        {
            EmitOpCode(builder, code);
        }
        else
        {
            if (unchecked((byte)value == value))
            {
                EmitOpCode(builder, ILOpCode.Ldc_i4_s);
                builder.WriteByte(unchecked((byte)value));
            }
            else
            {
                EmitOpCode(builder, ILOpCode.Ldc_i4);
                builder.WriteInt32(value);
            }
        }
    }
}
