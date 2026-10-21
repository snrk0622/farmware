// このファイルは、World Generation Runtime Assemblyの内部APIを同機能のEditModeテストとEditor診断から利用可能にします。
// 製品Assembly同士の依存は増やさず、大容量Native作業配列を呼出し間で再利用する友達Assemblyだけを宣言します。

using System.Runtime.CompilerServices;

// テストと製品ビルドへ含まれないEditor Assemblyだけを許可し、Runtimeの内部APIを製品コードの公開契約へ広げません。
[assembly: InternalsVisibleTo("Farmware.WorldGeneration.Tests")]
[assembly: InternalsVisibleTo("Farmware.WorldGeneration.Editor")]
