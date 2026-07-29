# Правила проекта для Codex

Полные правила разработки находятся в `Docs/Prototype/PROJECT_RULES.md` и являются обязательными.
Перед изменениями также прочитать `Docs/Prototype/README.md`, `DECISIONS.md`, `REQUIREMENTS.md`,
`ARCHITECTURE.md`, `ROADMAP.md`, `CONTENT_SCHEMA.md` и `ACCEPTANCE_TESTS.md`.

Ключевые ограничения: не менять Unity `6000.5.5f1`, Render Pipeline или зависимости без согласования;
не хранить runtime-состояние в ScriptableObject; использовать общую модель боевой единицы для команд;
отделять чистую Domain-логику от MonoBehaviour; сохранять `.meta`; не коммитить `Library`, `Temp`,
`Logs`, `Obj`, сборки и пользовательские файлы IDE.
