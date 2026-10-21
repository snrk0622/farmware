// このファイルは、World Generation Runtime Assemblyの内部APIを同機能のEditModeテストから検証可能にします。
// 製品Assembly同士の依存は増やさず、大容量Native作業配列を性能テストで再利用するための友達Assemblyだけを宣言します。

using System.Runtime.CompilerServices;

// テスト以外のAssembly名は追加せず、Runtimeの内部APIを製品コードの公開契約へ広げません。
[assembly: InternalsVisibleTo("Farmware.WorldGeneration.Tests")]
