# Prompt plantilla por HU

Copia, cambia el ID y pega en una conversación nueva en **modo plan**.

## Versión estándar
```
Implementa HU-0XX usando la skill hu-implementation.

1. Lee la HU completa en docs/backlog/, sus dependencias y las skills que lista.
2. Lee el código existente relacionado antes de proponer nada (busca con grep, no supongas nombres).
3. Dame el plan: archivos exactos, mensajes de protocolo con JSON de ejemplo, migraciones, lista de tests
   (uno por criterio de aceptación + casos de abuso) y riesgos.
4. Cuando lo apruebe: tests primero (compruébalos en rojo), luego implementación por capas
   (Content → Protocol → Game → Server → Persistence → client).
5. Verificación completa (build -warnaserror, dotnet test, validador de contenido, GUT) y server-authority-reviewer
   si tocaste server/.
6. Cierra la HU (DoD de la skill) y propón el mensaje de commit.

Si algo de la HU es ambiguo o contradice la arquitectura, pregúntame antes de decidir.
```

## Versión para HUs grandes (L)
```
HU-0XX es grande. Antes de planificar:
- Propón dividirla en 2–4 sub-entregas que cada una deje el juego funcionando y con tests verdes
  (ejemplo: "A: dominio + tests", "B: red + servidor", "C: cliente/UI").
- Para cada sub-entrega: alcance, criterios de aceptación cubiertos y demo manual.
Implementaremos una sub-entrega por conversación. Empieza por la A cuando apruebe.
```

## Versión "tests primero con subagente"
```
Usa el subagente qa-test-writer para escribir los tests de HU-0XX a partir de sus criterios de aceptación.
Revisa tú sus tests (¿prueban comportamiento y no implementación?, ¿faltan casos de abuso?) y muéstrame el resumen.
Después implementa hasta ponerlos en verde siguiendo hu-implementation.
```

## Continuar una HU a medias
```
Estamos a mitad de HU-0XX. Lee la HU, `git log --oneline -15` y `git diff main --stat`.
Dime qué criterios de aceptación ya están cubiertos (con el test que lo prueba), cuáles faltan y el siguiente paso.
```

## Cierre y revisión de la HU
```
Revisa HU-0XX como si fueras un revisor exigente:
- ¿Cada criterio de aceptación tiene test o verificación manual documentada?
- ¿Se cumplen las reglas no negociables de CLAUDE.md?
- ¿La documentación (protocol.md, database.md, design/) quedó al día?
Lista los problemas y corrígelos; luego marca la HU como Hecha.
```
