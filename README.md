# SMB Linker for Windows

**Keep your NAS / SMB network drives connected on Windows — reliably.**

[日本語はこちら](#日本語)

> Formerly **SmbAutoMount**. Installing 1.1.0 or later upgrades it in place and keeps your drives, passwords and settings.

SMB Linker is a small tray app for Windows 10/11 that mounts your NAS shares (TrueNAS, Synology, QNAP, Samba, Windows shares…) as drive letters and keeps them mounted.

It exists because Windows' own "Reconnect at sign-in" often fails: it tries to restore the drive **before the network is ready**, and you get *"The password is incorrect"*, a red ✖ on the drive, or error **1219** ("multiple connections to a server by the same user, using more than one user name") — even though your password is correct.

## Features

- **Waits for the network** — connects only after the server answers on TCP 445. Reconnects automatically after sign-in, wake from sleep, network changes and screen unlock, plus a periodic health check.
- **Diagnose & repair** — step-by-step checks (name resolution, port 445, saved password, drive letter, stale mappings, conflicting sessions, real sign-in test) with one-click repair. Error 1219 is fixed by closing only the connections that use a *different* user name.
- **Custom drive name and icon in File Explorer** — something Windows has no UI for. 13 built-in flat icons, or your own `.ico`.
- **Notifications** — tray notifications when a drive disconnects, reconnects or fails to sign in.
- **No account lockouts** — a rejected password is never retried automatically.
- **Native Windows 11 look** — WinUI 3, Mica, settings-style cards, light/dark theme. Japanese and English.

![Built-in drive icons](docs/icons-preview.png)

## Install

1. Download `SMBLinker-Setup-x.y.z-x64.exe` (or `-arm64`) from [Releases](https://github.com/aruiki/smb-linker-windows/releases).
2. Run it. **No administrator rights needed** — it installs for your user only.
3. Click **Add drive**, enter the server (you can paste `\\server\share`), user name and password, then **Test connection** and **Save**.

> Windows SmartScreen may warn about an unknown publisher because the installer is not code-signed yet. Choose *More info → Run anyway*.

Uninstalling disconnects the drives and removes the saved passwords and Explorer customisations.

## Security

- Passwords are stored only in **Windows Credential Manager** (encrypted with your Windows account via DPAPI). They are never written to config files or logs.
- Configuration (`%APPDATA%\SMBLinker\config.json`) contains server, share, drive letter, user name and display settings — no secrets.
- The app runs as your user (never elevated) and only changes per-user settings (`HKCU`).
- Diagnostics reports you copy for bug reports contain no password.

## How it works

| Problem | What SMB Linker does |
|---|---|
| Windows restores drives before the network is up | Removes Windows' own persistent mapping for the drive and maps it itself once `\\server` answers on port 445 |
| Error 1219 | Detects sessions to the same server with a different user name and closes them, then retries |
| Wrong user sent (e.g. Microsoft account) | Always sends the configured user, qualified as `server\user` |
| Stale "Unavailable" drive | Detects it and recreates the mapping |

## Build from source

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download), [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```powershell
.\build.ps1            # publish + installer for x64  -> dist\
.\build.ps1 -Arch arm64
dotnet build src\SMBLinker -c Debug   # just the app
```

Icons are generated from code: `powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1`.

## License

[MIT](LICENSE). Icons are original artwork included under the same license.

---

## 日本語

**Windows で NAS / SMB のネットワークドライブを確実につなぎっぱなしにするアプリです。**

> 旧名 **SmbAutoMount**。1.1.0 以降をインストールすると、登録したドライブ・パスワード・設定を引き継いだまま上書きアップデートされます。

TrueNAS・Synology・QNAP・Samba・Windows共有などの共有フォルダをドライブ文字に割り当て、接続を維持します。

Windows標準の「サインイン時に再接続する」は、**ネットワークの準備ができる前に**ドライブを復元しようとして失敗しがちです。その結果、パスワードは正しいのに「パスワードが違います」と言われたり、ドライブに赤い✖が付いたり、エラー **1219**（同じサーバーに別のユーザー名で接続できません）が出たりします。SMB Linker はこの問題を根本から解決します。

### 主な機能

- **ネットワークを待ってから接続** — サーバーがポート445に応答してから接続します。サインイン時・スリープ復帰時・ネットワーク切り替え時・ロック解除時に自動で再接続し、定期的に状態も確認します。
- **診断と自動修復** — 名前解決、ポート445、保存されたパスワード、ドライブ文字、古い割り当て、競合する接続、実際の認証テストを順に確認し、ワンクリックで修復します。エラー1219は「別のユーザー名の接続」だけを切断して解消します。
- **エクスプローラーでのドライブ名・アイコン変更** — Windows標準ではできない設定です。内蔵アイコン13種類、または自分の `.ico` を使えます。
- **通知** — 切断・再接続・認証失敗をトレイ通知でお知らせします。
- **アカウントロックを防止** — 拒否されたパスワードで自動再試行はしません。
- **Windows 11 純正の見た目** — WinUI 3、Mica、設定アプリ風のカード、ライト/ダークテーマ。日本語・英語対応。

### インストール

1. [Releases](https://github.com/aruiki/smb-linker-windows/releases) から `SMBLinker-Setup-x.y.z-x64.exe`（ARM版は `-arm64`）をダウンロードします。
2. 実行します。**管理者権限は不要**です（ユーザー単位でインストールされます）。
3. 「ドライブを追加」でサーバー（`\\server\share` の貼り付けも可）、ユーザー名、パスワードを入力し、「接続テスト」→「保存」。

> インストーラーはまだコード署名していないため、SmartScreen の警告が出ることがあります。「詳細情報」→「実行」を選んでください。

アンインストールすると、ドライブの切断・保存したパスワードとエクスプローラー設定の削除まで行います。

### セキュリティ

- パスワードは **Windows資格情報マネージャー** にのみ保存されます（Windowsアカウントに紐づいて DPAPI で暗号化）。設定ファイルやログには書きません。
- 設定ファイル（`%APPDATA%\SMBLinker\config.json`）にはサーバー、共有名、ドライブ文字、ユーザー名、表示設定だけが入ります。
- アプリは常に通常ユーザー権限で動き、ユーザー単位の設定（`HKCU`）だけを変更します。
- 不具合報告用にコピーできる診断レポートにはパスワードは含まれません。

### ライセンス

[MIT](LICENSE)。アイコンは本プロジェクトのオリジナル作品で、同じライセンスで提供します。
