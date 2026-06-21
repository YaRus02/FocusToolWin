FocusTool
=========

FocusTool - портативная Windows-утилита для презентаций, занятий, разборов,
созвонов, записи видео и работы с экраном.

Приложение добавляет:

- лазерную указку;
- аннотации поверх экрана;
- исчезающие аннотации;
- spotlight;
- лупу;
- live pinned lens;
- region masks;
- screen board / black board / white board;
- скриншоты с автоматическим сохранением и копированием в буфер;
- компактную overlay-панель;
- tray menu;
- настраиваемые глобальные хоткеи.


1. Какой файл запускать
-----------------------

Есть две сборки:

  self-contained\FocusTool.exe

    Полная автономная версия. Не требует установленного .NET Runtime.
    Это основной вариант для обычного использования.

  framework-dependent\FocusTool.exe

    Облегчённая версия. Требует установленный .NET Desktop Runtime подходящей
    версии. Удобна, если runtime уже установлен.

Для обычного использования запускайте:

  self-contained\FocusTool.exe

После запуска FocusTool появляется в системном трее. Если значок не виден,
проверьте скрытые значки трея Windows.


2. Основные хоткеи
------------------

  Ctrl+Alt+L        Переключить режим лазера Always / Hold
  Ctrl+Alt+D        Включить / выключить аннотации
  Ctrl+Alt+S        Spotlight
  Ctrl+Alt+M        Лупа
  Ctrl+Alt+P        Новая pinned lens
  Ctrl+Alt+H        Новая region mask
  Ctrl+Alt+Shift+H  Очистить все region masks
  Ctrl+Alt+F        Исчезающие аннотации
  Ctrl+Alt+T        Показать / скрыть overlay toolbar
  Ctrl+Alt+C        Скриншот текущего монитора
  Ctrl+Alt+G        Screen board
  Ctrl+Alt+B        Black board
  Ctrl+Alt+W        White board
  Ctrl+Alt+Q        Выход из FocusTool

Лазер в Hold mode:

  XButton2          Удерживать лазер

Аннотации:

  A                 Arrow
  R                 Rectangle
  C                 Ellipse / Circle
  L                 Line
  P                 Pencil
  H                 Highlighter
  T                 Text
  M                 Move selection
  1..5              Цветовые слоты аннотаций
  [ / ]             Уменьшить / увеличить толщину линии
  Ctrl+Z / Ctrl+Y   Undo / Redo
  Backspace         Удалить выделенное
  Delete или E      Очистить аннотации
  Esc               Выйти из визуального режима

Все хоткеи можно изменить в Settings.


3. Overlay toolbar
------------------

Открыть toolbar:

  Ctrl+Alt+T

Основные кнопки:

  Laser  Draw  Spot  Zoom  Pin  Mask  Board  Shot  ...

У каждой основной группы есть маленькая кнопка настроек. Она открывает
контекстный ряд:

- Laser: режим активации, цветовые слоты, glow, trail length.
- Draw: инструменты, цветовые слоты аннотаций, размер линии, размер текста,
  Fade, Undo, Redo, Clear.
- Spot: radius и dim amount.
- Zoom: zoom и radius лупы.
- Pin: базовый zoom/FPS новых pinned lens и close all.
- Mask: цветовые слоты маски, opacity, clear.
- Board: screen board, black board, white board.

Панель можно свернуть в маленький grip FT. Grip можно перетаскивать, а клик по
нему раскрывает панель обратно.


4. Цветовые слоты
-----------------

FocusTool использует настраиваемые цветовые слоты вместо фиксированных названий
цветов.

- Laser: Color 1..5.
- Annotation: Color 1..5.
- Region Mask: Color 1..5.

Цвета редактируются только в Settings через HEX:

  #FFFF2020
  #FF2080FF
  #FFFFFFFF

Toolbar и tray используют те же слоты. После Apply / OK цвет сразу становится
доступен во всех слоях управления.


5. Live Pinned Lens
-------------------

Live Pinned Lens показывает выбранную область экрана увеличенной в отдельном
живом плавающем окне.

Запуск:

  Ctrl+Alt+P

Затем выделите прямоугольник на экране.

Поддерживается:

- несколько окон одновременно;
- перетаскивание;
- контекстное меню по ПКМ;
- Freeze / Resume;
- Zoom in / Zoom out;
- Ctrl + колесо мыши для zoom конкретной линзы;
- Close / Close all.


6. Region Mask
--------------

Region Mask скрывает выбранные области экрана.

Запуск:

  Ctrl+Alt+H

Поведение:

- одно выделение создаёт одну маску и возвращает к обычному режиму;
- в режиме Mask маску можно двигать за тело;
- размер меняется за углы;
- ПКМ по маске удаляет её через контекстное меню;
- все маски очищаются через Ctrl+Alt+Shift+H или tray;
- новые маски используют текущий цветовой слот и opacity.

Маски видны в лупе, pinned lens, скриншотах и screen board, чтобы zoom не
раскрывал скрытую область.


7. Исчезающие аннотации
-----------------------

Переключение:

  Ctrl+Alt+F

Когда режим включён, новые аннотации становятся временными: они отображаются
полностью заданное время, а затем плавно исчезают.

В toolbar:

  Draw -> Fade

переключает режим. Маленькая кнопка "v" рядом с Fade открывает быстрые настройки
visible time и fade duration.


8. Скриншоты и доски
--------------------

Скриншот текущего монитора:

  Ctrl+Alt+C

Изображение сохраняется в:

  Pictures\FocusTool

и копируется в буфер обмена.

Screen Board при выходе автоматически сохраняет и копирует итоговую картинку.
Black Board и White Board не сохраняются автоматически.


9. Настройки и файлы
--------------------

Настройки:

  %APPDATA%\FocusTool\settings.json

Лог ошибок:

  %APPDATA%\FocusTool\log.txt

Скриншоты и доски:

  Pictures\FocusTool

Если Pictures недоступна, используется Documents.


10. Запись и стриминг
---------------------

FocusTool рисует как desktop overlay. Для OBS и похожих программ используйте
Display Capture / Monitor Capture, если нужно записать overlay. Window Capture
может не включать overlay-окна.


11. SmartScreen
---------------

FocusTool - небольшая неподписанная утилита. Windows SmartScreen может показать
предупреждение для нового неподписанного exe. Это ожидаемо.


12. Удаление
------------

FocusTool не требует установки.

1. Закройте FocusTool через tray или Ctrl+Alt+Q.
2. Удалите папку с exe.
3. При необходимости удалите:

   %APPDATA%\FocusTool

4. При необходимости удалите сохранённые изображения:

   Pictures\FocusTool
