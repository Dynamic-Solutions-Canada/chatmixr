<#
.SYNOPSIS
    Lists the Game Bar SDK's private interfaces as Package.appxmanifest <Interface> lines.
.DESCRIPTION
    The widget's package registers metadata-based marshaling for every private interface of the Game
    Bar SDK (the proxyStub extension in ChatMixrMSIX/Package.appxmanifest). The SDK's readme lists them
    too, but 7.3.2607010's list gives one interface another's id, so the list is read from the SDK's own
    private winmd instead. Run it after updating Microsoft.Gaming.XboxGameBar, and paste its output over
    the <Interface> lines. Windows PowerShell 7; ASCII only.
.EXAMPLE
    pwsh -File ChatMixr.GameBar/list-private-interfaces.ps1 -Path "$env:USERPROFILE\.nuget\packages\microsoft.gaming.xboxgamebar\<version>\private\Microsoft.Gaming.XboxGameBar.Private.winmd"
#>
param([Parameter(Mandatory)][string]$Path)

$ErrorActionPreference = "Stop"
$stream = [System.IO.File]::OpenRead($Path)
$pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $lines = foreach ($handle in $md.TypeDefinitions) {
        $type = $md.GetTypeDefinition($handle)
        $name = $md.GetString($type.Namespace) + "." + $md.GetString($type.Name)
        if (-not $md.GetString($type.Name).StartsWith("I")) { continue }   # interfaces only, as the readme lists
        foreach ($attributeHandle in $type.GetCustomAttributes()) {
            $attribute = $md.GetCustomAttribute($attributeHandle)
            if ($attribute.Constructor.Kind -ne [System.Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $constructor = $md.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($constructor.Parent.Kind -ne [System.Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $attributeType = $md.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$constructor.Parent)
            if ($md.GetString($attributeType.Name) -ne "GuidAttribute") { continue }

            # WinRT's GuidAttribute blob: prolog (2 bytes), then UInt32, UInt16, UInt16 and 8 bytes.
            $b = $md.GetBlobBytes($attribute.Value)
            $tail = ($b[10..17] | ForEach-Object { $_.ToString("X2") }) -join ""
            $id = "{0:X8}-{1:X4}-{2:X4}-{3}-{4}" -f [BitConverter]::ToUInt32($b, 2), [BitConverter]::ToUInt16($b, 6), [BitConverter]::ToUInt16($b, 8), $tail.Substring(0, 4), $tail.Substring(4)
            "        <Interface Name=""$name"" InterfaceId=""$id"" />"
        }
    }
    $lines | Sort-Object
} finally {
    $pe.Dispose()
    $stream.Dispose()
}
