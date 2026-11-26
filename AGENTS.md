# Unity changes

Unity can compile while a file is being edited. Prepare changes to C# scripts, assembly definitions, packages, or generated scenes in a temporary project outside the Unity checkout, especially when a method and its callers change together.

1. Copy `Assets`, `Packages`, and `ProjectSettings` into a temporary directory. Keep new assets and their `.meta` files together.
2. Make the full change in that directory. If an editor builder generates scenes or assets, run it there too.
3. From the repository, run `bash Tools/ValidateUnity.sh /path/to/temporary-project`. Run the relevant Unity play-mode validation in the temporary project when changing gameplay or a scene.
4. After validation passes, copy the changed files into the checkout in one operation. Check the diff for unrelated changes and verify recompilation if Unity is open.

If Unity validation is blocked, report the blocker and keep the unverified Unity edits in the temporary project. The validation script accepts `UNITY_EDITOR` as the path to the Unity executable if it is installed outside the macOS Unity Hub default location.
