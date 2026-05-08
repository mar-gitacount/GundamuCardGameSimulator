# ユニット攻撃〜OnAction〜戦闘解決（メソッドフロー）

VS Code / Cursor でこのファイルを開き、プレビューで Mermaid を表示してください。

- **推奨拡張**: [Markdown Preview Mermaid Support](https://marketplace.visualstudio.com/items?itemName=bierner.markdown-mermaid)  
  （または標準の Markdown プレビューが Mermaid に対応している環境でも可）

プレビュー: `Ctrl+Shift+V`（Windows） / 右上のプレビューアイコン

---

## プレイヤー・ユニット攻撃〜OnAction〜相互ダメージ

```mermaid
flowchart TD
    subgraph entry["入口（例）"]
        A["TryHandlePendingUnitAttackTarget<br/>または OpenEnemyUnitAttackTargetSelectionUI"]
    end

    subgraph uvu["ユニット対ユニット"]
        B["TryUnitVsUnitAttack"]
        C{"TryOpenOnAttackEnemySelectionPanel<br/>要る？"}
        D["TryRunAttackActionSteps<br/>onComplete → TryUnitVsUnitAttack(..., true)"]
    end

    subgraph ras["TryRunAttackActionSteps"]
        E["TryHandleSingleSideOnActionStep<br/>防御 attack:defender"]
        F["TryOpenOnActionCommandSelection"]
        G{"ユーザー: Close または一覧クリック"}
        H["TryHandleSingleSideOnActionStep<br/>攻撃 attack:attacker"]
        I["onComplete.Invoke"]
    end

    subgraph exec["選んだとき（コマンド／場ユニット）"]
        J["TryExecuteOnActionCommand"]
        K{"EnemyUnit<br/>効果がある？"}
        L["OpenOnActionEnemyTargetSelection"]
        M["ApplyEffectToSpecificTargets<br/>または ApplyEffect"]
        N["FinalizeOnActionSourceCard<br/>（Commandのみトラッシュ）"]
    end

    subgraph battle["戦闘解決 skipOnActionPause 後"]
        R["ApplyOnAttackEffectsForCombatPair"]
        S["Combat値確定 / OnAttackFallback 等"]
        T["ApplyDamage ×2"]
        U["SendCardToTrash・Clear・Sync"]
    end

    A --> B
    B --> C
    C -->|パネル表示| B
    C --> D
    D -->|モーダル表示で return| E
    E --> F
    F --> G
    G -->|Close| H
    G -->|クリック| J
    J --> K
    K -->|あり| L --> M --> N
    K -->|なし TryConsume→ApplyEffect| M
    N -->|onDone| F
    L -->|onDone| F
    F --> H
    H --> I
    I --> B

    B -->|"skip が true で<br/>TryRunAttackActionSteps 通過済み"| R --> S --> T --> U
```

---

## シールド攻撃（参考）

`TryUnitVsUnitAttack` の代わりに **`TryUnitShieldAttackFromUnit`** が入口。  
`TryRunAttackActionSteps` の `onComplete` は **`TryUnitShieldAttackFromUnit(attacker, true, true)`**。  
続きは **`gundamRule.TryApplyUnitShieldAttack`** → `TriggerCardEffects(OnAttack)` 等（ユニット同士の相互 `ApplyDamage` はなし）。

---

## メソッド早見（ユニット攻撃）

| 段 | メソッド |
|----|----------|
| 入口 | `TryHandlePendingUnitAttackTarget` / `OpenEnemyUnitAttackTargetSelectionUI` |
| 中核 | `TryUnitVsUnitAttack` |
| OnAttack選択 | `TryOpenOnAttackEnemySelectionPanel` |
| アクション | `TryRunAttackActionSteps` → `TryHandleSingleSideOnActionStep` → `TryOpenOnActionCommandSelection` |
| 効果適用 | `TryExecuteOnActionCommand` → `OpenOnActionEnemyTargetSelection`（任意）→ `ApplyEffect` / `ApplyEffectToSpecificTargets` |
| 再開 | `onComplete` → `TryUnitVsUnitAttack(..., true)` |
| 解決 | `ApplyOnAttackEffectsForCombatPair` → `ApplyDamage` |
