# Sample Import Package

This repository includes a minimal end-to-end sample package under [Samples\ImportPackage](D:\Quiz_Application\QuizAPI\Samples\ImportPackage).

Files:
- `quiz.csv`
- `forklift-safety.png`

The sample CSV demonstrates:
- required columns: `QuizTitle`, `QuestionText`, `AnswerText`, `IsCorrect`
- optional columns: `Category`, `QuestionImgKey`
- a question with an attached image using `QuestionImgKey=forklift-safety`

`QuestionImgKey` matching rules in the current importer:
- it may be the exact file name, like `forklift-safety.png`
- it may be the basename without extension, like `forklift-safety`
- it may contain multiple image keys separated by `|`, `;`, or `,`

Supported package image types are PNG, JPG/JPEG, GIF, and WebP. SVG files are rejected because uploaded SVG can execute as same-origin script in some browser paths. The importer also enforces package size, entry count, total uncompressed size, image count, per-image size, and image file-signature checks before extracting anything under `wwwroot/uploads`.

## Build The Sample ZIP

The verification script rebuilds the package automatically, but you can also build it manually from PowerShell:

```powershell
Compress-Archive -Path .\Samples\ImportPackage\* -DestinationPath .\Samples\ImportPackage\sample-import-package.zip -Force
```

## End-To-End Verification

Run the API locally, then execute:

```powershell
.\scripts\Test-ImportPackage.ps1 -BaseUrl https://localhost:5001 -AdminEmail admin@example.com -AdminPassword "YourPassword"
```

Or, if you already have a JWT:

```powershell
.\scripts\Test-ImportPackage.ps1 -BaseUrl https://localhost:5001 -AdminToken "<jwt>"
```

What the script verifies:
- uploads the sample ZIP through `POST /api/import/upload-package`
- processes the returned CSV through `POST /api/import/process/{fileName}`
- fetches the imported quiz through `GET /api/quiz?category=Safety`
- fetches quiz content through `GET /api/quiz/{quizId}/random`
- asserts that at least one question includes an image with a non-empty `Url`

Expected sample quiz title:
- `Sample Safety Quiz`
