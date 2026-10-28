// このファイルは、World Generation Presentation Assemblyの内部検証APIをEditorとEditModeテストへだけ公開します。
// 製品Assemblyの公開APIを増やさず、表示カタログの公開処理とテストがInspector相当の入力を設定できるようにします。

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Farmware.WorldGeneration.Editor")]
[assembly: InternalsVisibleTo("Farmware.WorldGeneration.Tests")]
