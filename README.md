# Jekyller

Avalonia 桌面應用：一站式 Jekyll 工具，方便：

- **一鍵建立 Jekyll 環境**（檢查 Ruby / Gem / Bundler / Jekyll / Git / gh）
- **編輯站台設定** `_config.yml`（常用欄位 + 原始 YAML）
- **安裝與設定 Themes**（Chirpy、Minima、Minimal Mistakes、Cayman…）
- **編輯 Markdown 內容**（posts / pages）
- **上傳到 GitHub** 並 **查詢 / 啟用 GitHub Pages 狀態**

## 需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ruby](https://rubyinstaller.org/)（含 DevKit，Windows 建議 Ruby+Devkit）
- Git
- [GitHub CLI](https://cli.github.com/)（部署用，需先 `gh auth login`）

## 執行

```bash
dotnet restore
dotnet run
```

## 建議使用流程

1. **環境建立** → 檢查工具 → 必要時安裝 Jekyll / Bundler → `jekyll new` 建立站台  
2. **主題 Themes** → 安裝 **Chirpy**（或其它主題）  
3. **站台設定** → 填寫 `title` / `url` / `baseurl` 等  
4. **內容 Markdown** → 建立文章與頁面  
5. **GitHub Pages** → 建立 repo 並推送 → 啟用 Pages → 查詢狀態  

## 專案結構

```
Jekyller/
  Models/          # 資料模型
  Services/        # Jekyll / Theme / Config / GitHub / Content
  ViewModels/      # MVVM
  Views/           # Avalonia XAML
```

## 注意事項

- Chirpy 完整功能在 GitHub Pages 上建議使用 [chirpy-starter](https://github.com/cotes2020/chirpy-starter) 或官方文件流程；空資料夾會以 starter clone，既有站台則設定 `remote_theme`。
- GitHub Pages 查詢依賴 `gh api repos/{owner}/{repo}/pages`。
- 本工具會在本機呼叫 `jekyll`、`bundle`、`git`、`gh` 等 CLI。
