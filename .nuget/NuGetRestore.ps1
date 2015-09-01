$NuGetExe = "$PSScriptRoot\NuGet.exe"
$NuGetRoot = "$env:UserProfile\.nuget\packages"

& $NuGetExe restore "$PSScriptRoot\packages.config" -PackagesDirectory $NuGetRoot -ConfigFile "$PSScriptRoot\NuGet.config"
& $NuGetExe restore "$PSScriptRoot\..\Roslyn.sln" -PackagesDirectory $NuGetRoot -ConfigFile "$PSScriptRoot\NuGet.config"
& $NuGetExe restore "$PSScriptRoot\..\src\Samples\Samples.sln" -PackagesDirectory $NuGetRoot -ConfigFile "$PSScriptRoot\NuGet.config"
& $NuGetExe restore "$PSScriptRoot\..\src\Dependencies\Dependencies.sln" -PackagesDirectory $NuGetRoot -ConfigFile "$PSScriptRoot\NuGet.config"

