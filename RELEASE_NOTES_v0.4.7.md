# AltPane 0.4.7

## 中文

0.4.6 在 Everything 已经通过 HTTP 返回结果时，路径行里的中文会变成 `?`，托盘 IPC 会被当成已连接，内置索引还会每隔几秒整棵重扫。0.4.7 修正这三处。窗口切换本身不变。

- **中文路径。** Everything 的 HTTP JSON 按 UTF-8 解码（显式的 Unicode 字符集优先）。搜索词按 UTF-8 做百分号编码。IPC 只用 Unicode 的 `QUERYW` / `LISTW`，不再把查询回显当成一份空结果。
- **传输。** 只有真正收到回复消息才算连上。已经返回过结果的传输（例如 `http:8080`）会被记住，空的 `ipc-taskbar` 不会再排到它前面。非空查询在已有可用传输时，不再相信另一路的 0 条结果。
- **内置索引。** Everything 可达时只保留快照，不做后台全盘扫描。普通文件变化按条更新。监视自己的目录和 AppData 事件会被忽略。缓冲区溢出后至少等 5 分钟才全量重扫，并释放旧的监视器。扫描结束后做一次压缩回收。文件结果只显示行号，不再显示整段拼音徽章。

## English

0.4.6 turned Chinese characters on the file-result path line into `?`, treated the Everything tray window as connected, and rescanned the built-in index every few seconds. 0.4.7 fixes those three. The window switcher itself is unchanged.

- **Chinese paths.** Everything HTTP JSON is decoded as UTF-8, unless the charset is an explicit Unicode encoding. The search is percent-encoded as UTF-8. IPC uses the Unicode `QUERYW` / `LISTW` structures, and a copy of the query is not an empty result list.
- **Transport.** A transport is connected only after a reply message arrives. A transport that has returned rows (for example `http:8080`) stays first. An empty `ipc-taskbar` result does not move ahead of it. A non-trivial query does not trust zero rows from another transport while a known-good one exists.
- **Built-in index.** While Everything is reachable, AltPane keeps the snapshot and does not walk the disk. Ordinary changes update one path. AltPane's own directories and AppData events are ignored. A watcher overflow waits at least five minutes before a full rescan, and the previous watchers are disposed. The scan compacts once when it finishes. File rows show the row number only, not a long pinyin badge.
