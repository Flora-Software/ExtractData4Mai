# ExtractData4Mai

用于从 maimai/Sinmai 的游戏资源目录中提取数据，并生成以下 JSON 文件：
1
- `partner.json`
- `plate.json`
- `title.json`
- `chara.json`
- `frame.json`
- `icon.json`
- `loginbouns.json`
- `NewMusic.json`
- `maps_data.json`

## 使用方法

### 方式一：显式传入输入目录和输出目录

```powershell
.\ExtractData4Mai.exe "D:\SDEZ165\Package\Sinmai_Data\StreamingAssets" "D:\output"
```

执行后会在 `D:\output` 下生成全部 JSON 文件。

### 方式二：只传输入目录

如果只传一个参数，程序会把输出写到当前工作目录。

```powershell
.\ExtractData4Mai.exe "D:\SDEZ165\Package\Sinmai_Data\StreamingAssets"
```

### 方式三：不传参数

如果不传参数，程序会默认把当前目录当作输入目录和输出目录。

这适合把程序放在 `StreamingAssets` 目录下直接运行。

## 输入目录要求

输入目录应为游戏资源根目录，例如：

`D:\SDEZ165\Package\Sinmai_Data\StreamingAssets`

