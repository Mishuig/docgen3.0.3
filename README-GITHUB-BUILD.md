# Як отримати готовий portable EXE через GitHub Actions

Цей репозиторій містить workflow `.github/workflows/build-windows.yml`. Він збирає WPF-застосунок на Windows runner GitHub, публікує self-contained `win-x64` і пакує його в ZIP. На комп'ютері користувача .NET SDK не потрібен.

## Один раз налаштувати GitHub

1. Увійдіть у GitHub і створіть новий **порожній** репозиторій (наприклад, `DocumentGenerator`). Для простоти можна зробити його **Private**.
2. Розпакуйте вихідний архів DocumentGenerator.
3. **Важливо — структура кореня репозиторію:**

   Завантажте **вміст** папки `DocumentGenerator/` (не саму папку) у **корінь** репозиторію.

   Правильно:
   ```
   ваш-репозиторій/
   ├── .github/
   │   └── workflows/
   │       └── build-windows.yml
   ├── DocumentGenerator.csproj
   ├── MainWindow.xaml
   ├── MainWindow.xaml.cs
   ├── Services/
   ├── samples/
   └── ...
   ```

   Неправильно (буде помилка `Project file does not exist`):
   ```
   ваш-репозиторій/
   └── DocumentGenerator/          ← зайва вкладена папка
       ├── DocumentGenerator.csproj
       └── ...
   ```

   Workflow також шукає `.csproj` у вкладеній папці `DocumentGenerator/`, але надійніше тримати файли в корені.

4. Зробіть **Commit changes** у гілку `main`.
5. Відкрийте вкладку **Actions**. Якщо GitHub попросить дозволити Actions — натисніть **I understand my workflows, go ahead and enable them**.
6. Виберіть workflow **Build portable Windows app** → **Run workflow** → **Run workflow**.
7. Дочекайтеся зеленого статусу (Success), відкрийте завершений запуск і внизу сторінки **Artifacts** завантажте `DocumentGenerator-0.3.1-portable-win-x64`.

Після кожного нового commit у `main` зі змінами коду/XAML/проєкту workflow також запускається автоматично.

## Запуск

1. Розпакуйте завантажений ZIP, наприклад у `C:\Apps\DocumentGenerator`.
2. Запустіть `DocumentGenerator.exe`.
3. Не запускайте програму з `C:\Program Files`: вона зберігає `data`, `templates`, `exports`, `backups` і `logs` поруч із EXE та потребує права запису.

Архів є self-contained для Windows x64 і не вимагає встановлення .NET Runtime або Microsoft Word. Microsoft Word потрібен лише якщо ви хочете відкривати DOCX у Word для ручного редагування.

## Якщо workflow впав з помилкою

### `MSBUILD : error MSB1009: Project file does not exist`
Файли лежать у вкладеній папці. Перемістіть увесь вміст `DocumentGenerator/` у корінь репозиторію (див. структуру вище) і зробіть новий commit.

### Інше
- Перевірте, що файл workflow має точний шлях `.github/workflows/build-windows.yml`.
- Переконайтеся, що GitHub Actions увімкнено в Settings → Actions → General.
- Відкрийте job `build`, розгорніть червоний крок і скопіюйте його лог.

Зверніть увагу: успішна компіляція підтверджує створення EXE, але перед використанням для юридично важливих документів потрібно перевірити конкретні шаблони й результати в Word/LibreOffice.
