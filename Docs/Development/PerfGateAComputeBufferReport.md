# PERF-GATE-A 終了時ComputeBuffer警告の切り分け

> 2026-10-19のゲーム方針改訂後も、本書の再現条件・切り分け結果を履歴として維持する。ANIMAL-01Aへの進行判断は当時のもので、現在の着手順は[改訂ロードマップ](./ImplementationRoadmap.md)を正とする。新たなUnity・パッケージ更新や表示寿命の変更時は、必要に応じ再確認する。

- 調査日: 2026-10-17
- 対象: Development Player終了時の`ComputeBuffer.Release()`／`Dispose()`警告
- 結論: Farmware所有リソースではなく、`com.unity.2d.animation` 13.0.4の未使用時にも生成されるGPU変形用フォールバックバッファが原因
- 判断: データ破損や実行中の継続増加を示す証拠はなく、`ANIMAL-01A`を止めない

## 1. 調査目的

MAP-02DのDevelopment Playerで、終了時に次の警告が1件記録されていた。

```text
GarbageCollector disposing of ComputeBuffer. Please use ComputeBuffer.Release() or .Dispose() to manually release the buffer.
```

PERF-GATE-Aでは、Farmwareの所有権・破棄不備か、Unity本体または導入パッケージの終了処理かを最小条件で切り分ける。外部要因の場合は、再現条件、版、影響範囲、再確認方法を残す。

## 2. 調査環境

| 項目 | 値 |
| --- | --- |
| OS／CPU | macOS／Apple M3 |
| Unity | 6000.3.8f1 |
| Player | macOS Development Build、Mono、Universal出力をArm64で実行 |
| 2D Animation | `com.unity.2d.animation` 13.0.4 |
| Entities | `com.unity.entities` 1.5.0 |
| URP | `com.unity.render-pipelines.universal` 17.3.0 |

Unityとパッケージ版は`ProjectSettings/ProjectVersion.txt`、`Packages/manifest.json`、`Packages/packages-lock.json`を正として確認した。

## 3. ソースと所有権の確認

`Assets/Farmware`には`ComputeBuffer`または`GraphicsBuffer`を生成する製品コードがなく、Sprite Skinを使用するScene、Prefab、Asset参照も見つからなかった。Farmwareが所有するMeshやNative Collectionには、それぞれの寿命に対応する既存の破棄処理がある。

固定版パッケージの`Library/PackageCache/com.unity.2d.animation@7396d8477ca9/Runtime/BatchedDeformation/GpuDeformationSystem.cs`では、`RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)`から静的な`s_FallbackBuffer`を1個生成する。バッファは`GpuDeformationSystem.Cleanup()`から解放される。一方、`DeformationManager.cs`の管理インスタンスは必要時に遅延生成され、その`OnDisable()`が各変形Systemの`Cleanup()`を呼ぶ構造である。

このため、Sprite Skinを一度も使わず`DeformationManager`が生成されないPlayerでもフォールバックバッファだけは生成され、通常の管理インスタンス経由の解放処理へ到達しない、という責任境界を仮説にした。

## 4. 最小再現とA/B比較

FarmwareのランタイムSceneオブジェクトを含まない空Sceneに、終了時の状態だけを記録する一時診断コードを配置した。同じPlayerバイナリを次の2条件で起動し、5フレーム後に`Application.Quit(0)`で正常終了させた。

- Control: 2D Animationの管理インスタンスを生成しない。
- Forced manager: 診断コードから反射で`UnityEngine.U2D.Animation.DeformationManager.instance`へ一度アクセスし、パッケージ自身の終了時`Cleanup()`経路を成立させる。

反射アクセスは原因確認だけに使用した。非公開実装への依存になるため、製品コードや恒久テストには採用しない。

| 条件 | 終了直前のフォールバックバッファ | 終了時警告 | 終了時診断のallocatedMemory |
| --- | --- | ---: | ---: |
| Control | 存在、`IsValid()` = true | 1件 | 3,408,446 bytes |
| Forced manager | 存在、`IsValid()` = true | 0件 | 3,407,595 bytes |

同一バイナリで変更したのは起動引数だけである。両条件とも`PlayerConnection::Cleanup##utp`の終了時JSONは出力されたため、このJSON自体をComputeBufferリークの証拠にはしない。allocatedMemoryの差は851 bytesだったが、Profilerの集計ラベルや終了順を含む値であり、バッファ本体の正確なサイズとは断定しない。

Controlだけで元の警告が再現し、パッケージ自身の管理インスタンスと終了処理を成立させた条件だけで消えた。空Sceneでも再現することと合わせ、原因はFarmwareのマップ表示やECSではなく、固定中の2D Animationパッケージにあると判断する。

## 5. 影響と対応判断

- 確認できた警告はプロセス終了時の1件であり、プレイ中に件数や使用量が増え続ける現象は確認していない。
- この調査範囲で、ゲーム状態の破損、保存データの破損、描画結果の欠落は確認していない。
- 現在のFarmwareはSprite Skinを使用しておらず、動物もPixelLabのスプライトシートを前提としている。
- 内部型を反射で生成する回避策は、非公開APIへの依存と不要な管理オブジェクト生成を持ち込むため採用しない。
- この警告だけを理由に、Unity本体や2D Animationパッケージの削除・更新は行わない。パッケージ操作は利用状況と互換性を別変更単位で確認し、ユーザー承認のもとで行う。

以上から、PERF-GATE-Aは責任境界の特定をもって完了とし、`ANIMAL-01A`へ進める。これは将来の長時間メモリ評価や、Sprite Skin採用時のGPU変形リソース監査を不要にする判断ではない。

## 6. 再確認の契機と手順

次の場合に再確認する。

1. Unityまたは`com.unity.2d.animation`を更新する。
2. Sprite Skin、2D Animation、GPU変形をFarmwareで実際に採用する。
3. 終了時警告が複数件へ増える、または実行中のメモリ増加が観測される。

更新時は、公式の[2D Animation 13.0 changelog](https://docs.unity3d.com/ja/Packages/com.unity.2d.animation@13.0/changelog/CHANGELOG.html)と[2D Animation package manual](https://docs.unity3d.com/ja/Packages/com.unity.2d.animation@13.0/manual/index.html)を確認し、まず空SceneのControlを再実行する。警告が残る場合は、固定された新しいパッケージソースの生成・解放経路を再確認する。調査時点では、公式changelogと公開Issue Tracker検索に一致する修正記録を確認できなかった。

## 7. 証跡と後片付け

調査用PlayerとログはGit対象外の次の場所へ保持した。

- ビルド結果: `/private/tmp/farmware-perf-gate-a.20261017-v3/build-result.txt`
- Controlログ: `/private/tmp/farmware-perf-gate-a.20261017-v3/control.log`
- Forced managerログ: `/private/tmp/farmware-perf-gate-a.20261017-v3/forced-manager.log`

一時的に追加した診断C#、`.meta`、空SceneはAssetsから削除した。ビルドで派生変更されたProjectSettingsとSprite Atlasは開始時の内容へ戻し、Farmwareの製品コード、Scene、Prefab、パッケージ構成は変更していない。

一時コード撤去後にUnity CLIから再コンパイルし、Unity 6000.3.8f1が停止状態、コンパイル失敗なし、Consoleエラー0であることを確認した。Consoleに残る警告は診断Playerビルド時の既知のRuntimePipelineConfig不足だけである。製品コードと自動テストを変更していないため、EditMode／PlayModeテストは再実行していない。
