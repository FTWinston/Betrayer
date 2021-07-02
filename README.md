# Betrayer
A Valheim game mode. Very much WIP. Doesn't do anything much, yet.

# Dev setup
* Install this pack, manually, on a dedicated server: https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/
* Open this project, and copy the "missing" DLLs from the pack (and/or your valheim folder) to a "lib" folder in this solution.
* Use the following:
 * BepInEx plugin guide: https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/index.html
 * HarmonyX patching guide: https://github.com/BepInEx/HarmonyX/wiki/Basic-usage
 * dotPeek to look through assembly_valheim.dll code: https://www.jetbrains.com/decompiler/

* Download and Unzip https://github.com/FTWinston/Betrayer/files/6756571/CopyDLLS.zip into the Betrayer project directory (next to the .csproj) and edit the target directory variable so it'll automatically copy your .dlls over to the install directory
