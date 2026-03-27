#!/usr/bin/env python3
"""
KodaClaw SQLite → JSON 一次性迁移脚本
将 control-plane.db 中的关键数据迁移到 .koda/store/ 和 config/models/ 目录。

用法：
  python3 migrate_sqlite_to_json.py /path/to/workspace

例如：
  python3 migrate_sqlite_to_json.py ~/.kodaclaw_dev/dev7
"""

import json
import os
import sqlite3
import sys
from pathlib import Path


# ─── 工具函数 ────────────────────────────────────────────────────────────────

def to_camel(s: str) -> str:
    """PascalCase → camelCase（JsonNamingPolicy.CamelCase 行为）"""
    if not s:
        return s
    return s[0].lower() + s[1:]


def write_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"  ✓ {path.relative_to(path.parents[4])}")


# ─── 迁移 model_endpoints → config/models/{id}.json ─────────────────────────

def migrate_models(conn: sqlite3.Connection, workspace: Path) -> int:
    models_dir = workspace / "config" / "models"
    rows = conn.execute("""
        SELECT id, display_name, provider, model_id, base_url,
               api_key_env, api_key_secret_ref, enabled, capabilities,
               is_default, created_at, updated_at,
               context_window_size, max_output_tokens, is_reasoning
        FROM model_endpoints
    """).fetchall()

    count = 0
    for row in rows:
        (id_, display_name, provider, model_id, base_url,
         api_key_env, api_key_secret_ref, enabled, capabilities,
         is_default, created_at, updated_at,
         context_window_size, max_output_tokens, is_reasoning) = row

        target = models_dir / f"{id_}.json"
        if target.exists():
            print(f"  - 跳过（已存在）: config/models/{id_}.json")
            continue

        data = {
            "id": id_,
            "displayName": display_name,
            "provider": to_camel(provider),          # "anthropic", "openAICompatible" 等
            "modelId": model_id,
            "baseUrl": base_url,
            "apiKeyEnvironmentVariable": api_key_env or None,
            "apiKeySecretRef": api_key_secret_ref or None,
            "enabled": bool(enabled),
            "capabilities": capabilities or 0,
            "isDefault": bool(is_default),
            "createdAt": created_at,
            "updatedAt": updated_at,
            "contextWindowSize": context_window_size or 128000,
            "maxOutputTokens": max_output_tokens or 8192,
            "isReasoning": bool(is_reasoning),
        }
        write_json(target, data)
        count += 1

    return count


# ─── 迁移 channel_accounts → .koda/store/channels/accounts/{id}.json ────────

def migrate_channel_accounts(conn: sqlite3.Connection, workspace: Path) -> int:
    accounts_dir = workspace / ".koda" / "store" / "channels" / "accounts"
    rows = conn.execute("""
        SELECT id, connector_kind, display_name, state,
               external_account_id, credential_reference, description,
               configuration_json, inbound_enabled,
               created_at, updated_at, last_connected_at,
               last_disconnected_at, last_error
        FROM channel_accounts
    """).fetchall()

    count = 0
    for row in rows:
        (id_, connector_kind, display_name, state,
         external_account_id, credential_reference, description,
         configuration_json, inbound_enabled,
         created_at, updated_at, last_connected_at,
         last_disconnected_at, last_error) = row

        target = accounts_dir / f"{id_}.json"
        if target.exists():
            print(f"  - 跳过（已存在）: .koda/store/channels/accounts/{id_}.json")
            continue

        data = {
            "id": id_,
            "connectorKind": to_camel(connector_kind),   # "telegram", "weChat", "feishu"
            "displayName": display_name,
            "state": to_camel(state),                    # "connected", "degraded" 等
            "createdAt": created_at,
            "updatedAt": updated_at,
            "externalAccountId": external_account_id or None,
            "credentialReference": credential_reference or None,
            "description": description or None,
            "configurationJson": configuration_json or None,
            "inboundEnabled": bool(inbound_enabled),
            "lastConnectedAt": last_connected_at or None,
            "lastDisconnectedAt": last_disconnected_at or None,
            "lastError": last_error or None,
        }
        write_json(target, data)
        count += 1

    return count


# ─── 迁移 thread_bindings → .koda/store/channels/bindings/{id}.json ─────────

def migrate_thread_bindings(conn: sqlite3.Connection, workspace: Path) -> int:
    bindings_dir = workspace / ".koda" / "store" / "channels" / "bindings"
    rows = conn.execute("""
        SELECT id, connector_kind, account_id, external_thread_id,
               thread_type, session_id, session_kind,
               channel_identity_json, policy_id, delivery_rule_id,
               created_at, updated_at, last_inbound_at,
               last_outbound_at, last_message_preview, delivery_mode_override
        FROM thread_bindings
    """).fetchall()

    count = 0
    for row in rows:
        (id_, connector_kind, account_id, external_thread_id,
         thread_type, session_id, session_kind,
         channel_identity_json, policy_id, delivery_rule_id,
         created_at, updated_at, last_inbound_at,
         last_outbound_at, last_message_preview, delivery_mode_override) = row

        target = bindings_dir / f"{id_}.json"
        if target.exists():
            print(f"  - 跳过（已存在）: .koda/store/channels/bindings/{id_}.json")
            continue

        # channel_identity_json 在 SQLite 里已是 camelCase JSON 字符串
        identity = json.loads(channel_identity_json) if channel_identity_json else {}

        data = {
            "id": id_,
            "connectorKind": to_camel(connector_kind),
            "accountId": account_id,
            "externalThreadId": external_thread_id,
            "threadType": to_camel(thread_type),             # "directMessage", "group"
            "sessionId": session_id,
            "sessionKind": to_camel(session_kind),           # "channelDirectMessage" 等
            "channelIdentity": identity,
            "policyId": policy_id,
            "deliveryRuleId": delivery_rule_id,
            "createdAt": created_at,
            "updatedAt": updated_at,
            "lastInboundAt": last_inbound_at or None,
            "lastOutboundAt": last_outbound_at or None,
            "lastMessagePreview": last_message_preview or None,
            "deliveryModeOverride": to_camel(delivery_mode_override) if delivery_mode_override else None,
        }
        write_json(target, data)
        count += 1

    return count


# ─── 迁移 canvas_artifacts → .koda/store/canvas/{id}.json ───────────────────

def migrate_canvas_artifacts(conn: sqlite3.Connection, workspace: Path) -> int:
    canvas_dir = workspace / ".koda" / "store" / "canvas"
    rows = conn.execute("""
        SELECT id, title, kind, summary, source, route,
               entry_path, asset_directory, session_id,
               correlation_id, created_at, updated_at, metadata_json
        FROM canvas_artifacts
    """).fetchall()

    count = 0
    for row in rows:
        (id_, title, kind, summary, source, route,
         entry_path, asset_directory, session_id,
         correlation_id, created_at, updated_at, metadata_json) = row

        target = canvas_dir / f"{id_}.json"
        if target.exists():
            print(f"  - 跳过（已存在）: .koda/store/canvas/{id_}.json")
            continue

        data = {
            "id": id_,
            "title": title,
            "kind": to_camel(kind),                   # "report", "image" 等
            "summary": summary,
            "source": source,
            "entryPath": entry_path,
            "assetDirectory": asset_directory,
            "createdAt": created_at,
            "updatedAt": updated_at,
            "route": route or None,
            "sessionId": session_id or None,
            "correlationId": correlation_id or None,
            "metadataJson": metadata_json or None,
        }
        write_json(target, data)
        count += 1

    return count


# ─── 主流程 ──────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    workspace = Path(sys.argv[1]).expanduser().resolve()
    db_path = workspace / "config" / "control-plane.db"
    marker_path = workspace / ".koda" / "store" / "migration-done"

    if not db_path.exists():
        print(f"✓ 未发现 {db_path}，无需迁移。")
        sys.exit(0)

    if marker_path.exists():
        print(f"✓ 迁移标记已存在（{marker_path}），跳过。")
        sys.exit(0)

    print(f"数据库: {db_path}")
    print(f"Workspace: {workspace}")
    print()

    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row

    try:
        print("── model_endpoints → config/models/ ──────────────────────")
        n = migrate_models(conn, workspace)
        print(f"  迁移 {n} 条\n")

        print("── channel_accounts → .koda/store/channels/accounts/ ─────")
        n = migrate_channel_accounts(conn, workspace)
        print(f"  迁移 {n} 条\n")

        print("── thread_bindings → .koda/store/channels/bindings/ ──────")
        n = migrate_thread_bindings(conn, workspace)
        print(f"  迁移 {n} 条\n")

        print("── canvas_artifacts → .koda/store/canvas/ ────────────────")
        n = migrate_canvas_artifacts(conn, workspace)
        print(f"  迁移 {n} 条\n")

    finally:
        conn.close()

    # 写迁移标记
    marker_path.parent.mkdir(parents=True, exist_ok=True)
    marker_path.write_text("migrated from control-plane.db\n")

    # 归档 db 文件
    archived = db_path.with_suffix(".db.pre-json")
    db_path.rename(archived)
    print(f"── 完成 ───────────────────────────────────────────────────────")
    print(f"  已归档原数据库: {archived.name}")
    print(f"  迁移标记: {marker_path}")
    print()
    print("✓ 迁移完成。")


if __name__ == "__main__":
    main()
