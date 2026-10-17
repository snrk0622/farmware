# Codexスキル

- 最終更新日: 2026-10-17
- 目的: Farmware専用Codexスキルの一覧、用途、呼び出し方、更新場所を一か所にまとめる。

## 保存場所

Farmware専用スキルは、リポジトリ直下の `.agents/skills/` を正とする。
個人領域の `~/.codex/skills/` ではなく、次のファイルを編集してGitで共有する。

```text
.agents/skills/
└── fixed-resource-prompt-dialogue/
    ├── SKILL.md
    ├── agents/openai.yaml
    └── references/template.md
```

Codexは、現在の作業ディレクトリからリポジトリルートまでの `.agents/skills/` を検索する。そのため、このリポジトリまたは配下のディレクトリを作業場所として開くと、Farmware専用スキルを利用できる。

## スキル一覧

| スキル | 用途 | 主な入力 | 出力 |
| --- | --- | --- | --- |
| `fixed-resource-prompt-dialogue` | 木、岩、草など、動かない固定資源のPixelLab Create Image用Promptを対話で作る | 資源名、配置分類、Output size、占有範囲、状態、外見、禁止事項 | 設定一覧と、全バリエーション・全状態・通常版・積雪版を作る英語Prompt |

### `fixed-resource-prompt-dialogue`

最初に、対象を次のどちらかへ分類する。

- `blocking_resource`: 木や岩など、施設配置を妨害する採取資源。露出した外周には `#333333` の輪郭を付け、地面との接地点には輪郭を付けない。
- `auto_clear_scenery`: 草など、施設配置時に自動除去される景観資源。輪郭を付けない。

`blocking_resource`では、通常状態に加えて伐採後や枯渇後など、その資源に必要な静止状態を提案してから採否を確認する。すべての基本バリエーションと状態に通常版と積雪版を用意し、雪の明部を `#EDF3F2`、影色を `#C5D6DE` に統一する。

動物、地形Base、アニメーション、画像生成、UnityへのImportは対象外とする。

## 使い方

### 1. 明示的に呼び出す

チャットで `$スキル名` に続けて依頼を書く。

```text
$fixed-resource-prompt-dialogue 雪原にも対応する針葉樹のPixelLab Promptを作成してください
```

情報が不足している場合、スキルが一問ずつ確認する。すでに伝えた値は再質問せず、必要な情報が揃っていれば完成Promptを直接出力する。

### 2. スキル一覧から選ぶ

Codexのスキル一覧を開き、目的に合うスキルを選択して依頼する。利用環境でスラッシュコマンドが使える場合は `/skills` から確認できる。

### 3. 自然文で依頼する

スキル名を書かなくても、説明と一致する依頼ではCodexが自動選択できる。ただし、確実に使用したい場合は `$fixed-resource-prompt-dialogue` を明記する。

### スキルが表示されない場合

次を順に確認する。

1. 作業場所がFarmwareリポジトリまたはその配下である。
2. `.agents/skills/<skill-name>/SKILL.md` が存在する。
3. `SKILL.md` のfrontmatterに `name` と `description` がある。
4. スキル一覧を更新するか、Codexを再起動する。

## 更新方法

1. 対象スキルの `.agents/skills/<skill-name>/SKILL.md` を更新する。
2. Promptテンプレートも変わる場合は、同じスキルの `references/template.md` を同時に更新する。
3. 表示名や初期依頼文を変える場合は、`agents/openai.yaml` を更新する。
4. `Docs/Art/TilemapSpriteProductionChecklist.md` の制作仕様と矛盾しないことを確認する。
5. スキル一覧や使い方が変わる場合は、この文書も同じ変更内で更新する。
6. `$skill-creator` を使って構造とfrontmatterを検証する。

個人領域に同名スキルが残っていると、環境によって一覧へ重複表示される可能性がある。Farmware向けの内容はリポジトリ版だけを更新し、個人版が不要であることを確認してから削除または別名へ変更する。

## 参考

- [OpenAI公式: Build skills](https://learn.chatgpt.com/docs/build-skills)
