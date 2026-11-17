using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ViShap.Rewind.Tests.Structure;

public sealed class StructureTests
{
    private const string FileType = "ViShap.Rewind.RewindFile";

    private static readonly string[] s_ioNamespaces = ["System.IO", "System.Net", "Microsoft.Win32"];

    private static readonly string[] s_ioTypes = ["System.Console", "System.Diagnostics.Process"];

    [Fact]
    public void CoreAssembly_ReferencesInputOutputOnlyFromRewindFile()
    {
        var uses = TypeUses(typeof(Rewind).Assembly.Location)
            .Where(use => IsInputOutput(use.Referenced))
            .ToList();

        var offending = uses
            .Where(use => use.User != FileType && !use.User.StartsWith(FileType + "+", StringComparison.Ordinal))
            .Select(use => $"{use.User} uses {use.Referenced}")
            .Distinct()
            .ToList();

        Assert.Contains(uses, use => use.User == FileType && use.Referenced == "System.IO.File");
        Assert.Empty(offending);
    }

    private static bool IsInputOutput(string name) =>
        s_ioTypes.Contains(name) || s_ioNamespaces.Any(ns => name.StartsWith(ns + ".", StringComparison.Ordinal));

    /// <summary>
    /// Returns every type an assembly's types reference — through their base types, interfaces, field and method
    /// signatures, local variables and the operands of their IL — with the full name of the type that references it.
    /// </summary>
    private static List<(string User, string Referenced)> TypeUses(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var names = new TypeNames(metadata);
        var uses = new List<(string, string)>();

        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            var user = FullName(metadata, typeHandle);
            void Add(IEnumerable<string> referenced) => uses.AddRange(referenced.Select(name => (user, name)));

            if (!type.BaseType.IsNil)
            {
                Add(names.Of(type.BaseType));
            }

            foreach (var interfaceHandle in type.GetInterfaceImplementations())
            {
                Add(names.Of(metadata.GetInterfaceImplementation(interfaceHandle).Interface));
            }

            foreach (var fieldHandle in type.GetFields())
            {
                Add(metadata.GetFieldDefinition(fieldHandle).DecodeSignature(names, null));
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                Add(Flatten(method.DecodeSignature(names, null)));
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                if (!body.LocalSignature.IsNil)
                {
                    Add(metadata.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(names, null).SelectMany(local => local));
                }

                foreach (var token in OperandTokens(body.GetILReader()))
                {
                    Add(names.Of(MetadataTokens.EntityHandle(token)));
                }
            }
        }

        return uses;
    }

    private static string FullName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return FullName(metadata, declaring) + "+" + metadata.GetString(type.Name);
        }

        var ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? metadata.GetString(type.Name) : ns + "." + metadata.GetString(type.Name);
    }

    private static IEnumerable<string> Flatten(MethodSignature<ImmutableArray<string>> signature) =>
        signature.ReturnType.Concat(signature.ParameterTypes.SelectMany(parameter => parameter));

    /// <summary>Returns the metadata tokens IL instructions take as operands, string tokens excepted.</summary>
    private static List<int> OperandTokens(BlobReader il)
    {
        var tokens = new List<int>();
        while (il.RemainingBytes > 0)
        {
            int code = il.ReadByte();
            if (code == 0xFE)
            {
                code = 0xFE00 | il.ReadByte();
            }

            var opCode = (ILOpCode)code;
            switch (OperandSize(opCode))
            {
                case OperandKind.Token:
                    var token = il.ReadInt32();
                    if (opCode != ILOpCode.Ldstr)
                    {
                        tokens.Add(token);
                    }

                    break;
                case OperandKind.Switch:
                    il.Offset += 4 * il.ReadInt32();
                    break;
                case OperandKind.One:
                    il.Offset += 1;
                    break;
                case OperandKind.Two:
                    il.Offset += 2;
                    break;
                case OperandKind.Four:
                    il.Offset += 4;
                    break;
                case OperandKind.Eight:
                    il.Offset += 8;
                    break;
            }
        }

        return tokens;
    }

    private static OperandKind OperandSize(ILOpCode code) => code switch
    {
        ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Calli or ILOpCode.Jmp
            or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
            or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Ldstr
            or ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Castclass or ILOpCode.Isinst
            or ILOpCode.Newarr or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem
            or ILOpCode.Ldobj or ILOpCode.Stobj or ILOpCode.Cpobj or ILOpCode.Initobj
            or ILOpCode.Mkrefany or ILOpCode.Refanyval or ILOpCode.Sizeof or ILOpCode.Constrained => OperandKind.Token,
        ILOpCode.Switch => OperandKind.Switch,
        ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s
            or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
            or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s
            or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s
            or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned => OperandKind.One,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => OperandKind.Two,
        ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt
            or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
            or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 => OperandKind.Four,
        ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => OperandKind.Eight,
        _ => OperandKind.None,
    };

    private enum OperandKind
    {
        None,
        One,
        Two,
        Four,
        Eight,
        Token,
        Switch,
    }

    /// <summary>Names the types referenced from other assemblies that a handle or a signature mentions.</summary>
    private sealed class TypeNames(MetadataReader metadata) : ISignatureTypeProvider<ImmutableArray<string>, object?>
    {
        public IEnumerable<string> Of(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeReference:
                    return [ReferenceName((TypeReferenceHandle)handle)];
                case HandleKind.TypeSpecification:
                    return metadata.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null);
                case HandleKind.MemberReference:
                    var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                    var signature = member.GetKind() == MemberReferenceKind.Method
                        ? Flatten(member.DecodeMethodSignature(this, null))
                        : member.DecodeFieldSignature(this, null);
                    return Of(member.Parent).Concat(signature);
                case HandleKind.MethodSpecification:
                    var specification = metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return Of(specification.Method).Concat(specification.DecodeSignature(this, null).SelectMany(argument => argument));
                case HandleKind.StandaloneSignature:
                    return Flatten(metadata.GetStandaloneSignature((StandaloneSignatureHandle)handle).DecodeMethodSignature(this, null));
                default:
                    return [];
            }
        }

        public ImmutableArray<string> GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            [ReferenceName(handle)];

        public ImmutableArray<string> GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => [];

        public ImmutableArray<string> GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public ImmutableArray<string> GetPrimitiveType(PrimitiveTypeCode typeCode) => [];

        public ImmutableArray<string> GetSZArrayType(ImmutableArray<string> elementType) => elementType;

        public ImmutableArray<string> GetArrayType(ImmutableArray<string> elementType, ArrayShape shape) => elementType;

        public ImmutableArray<string> GetByReferenceType(ImmutableArray<string> elementType) => elementType;

        public ImmutableArray<string> GetPointerType(ImmutableArray<string> elementType) => elementType;

        public ImmutableArray<string> GetPinnedType(ImmutableArray<string> elementType) => elementType;

        public ImmutableArray<string> GetModifiedType(ImmutableArray<string> modifier, ImmutableArray<string> unmodifiedType, bool isRequired) =>
            modifier.AddRange(unmodifiedType);

        public ImmutableArray<string> GetGenericInstantiation(ImmutableArray<string> genericType, ImmutableArray<ImmutableArray<string>> typeArguments) =>
            genericType.AddRange(typeArguments.SelectMany(argument => argument));

        public ImmutableArray<string> GetGenericMethodParameter(object? genericContext, int index) => [];

        public ImmutableArray<string> GetGenericTypeParameter(object? genericContext, int index) => [];

        public ImmutableArray<string> GetFunctionPointerType(MethodSignature<ImmutableArray<string>> signature) => [.. Flatten(signature)];

        private string ReferenceName(TypeReferenceHandle handle)
        {
            var type = metadata.GetTypeReference(handle);
            var name = metadata.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return ReferenceName((TypeReferenceHandle)type.ResolutionScope) + "+" + name;
            }

            var ns = metadata.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
    }
}
