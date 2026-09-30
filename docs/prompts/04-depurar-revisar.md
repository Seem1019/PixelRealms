# Prompts para depurar, revisar y hacer playtests

## Bug reproducible
```
Bug: <qué pasa> / esperado: <qué debería pasar> / pasos: <1,2,3> / logs: <pega el log del servidor y del cliente>.
1. Encuentra la causa raíz (no el síntoma): lee el código implicado y explica la cadena de eventos.
2. Escribe primero un test que reproduzca el bug y compruébalo en rojo.
3. Arréglalo con el cambio mínimo, pon el test en verde y ejecuta toda la suite.
4. ¿Puede haber el mismo bug en otros sitios? Búscalo.
```

## Desincronización de movimiento
```
Los jugadores "saltan" o atraviesan paredes en el cliente. Con la skill net-protocol §Depurar desincronización:
compara MovementStep.cs y movement_step.gd línea a línea, crea un vector de prueba nuevo en
shared/test-vectors/movement.json que reproduzca el caso y hazlo pasar en ambos lados.
```

## Rendimiento del tick
```
El tick p99 supera 20 ms con <N> jugadores. Ejecuta tools/LoadBot con 50 bots durante 2 minutos,
perfila el servidor (dotnet-counters / dotnet-trace) y dame el top 5 de puntos calientes con propuesta de arreglo
y la mejora esperada. No cambies nada hasta que elija.
```

## Auditoría de seguridad
```
Ejecuta el subagente server-authority-reviewer sobre TODO server/src (no solo el diff) y además busca:
duplicación de items, ataques de carrera entre dos conexiones de la misma cuenta, inyección en chat,
y mensajes que puedan lanzar excepciones en el tick. Crea una HU nueva en docs/backlog/ por cada hallazgo ALTO o CRÍTICO.
```

## Revisión de arquitectura periódica (cada hito)
```
Hemos terminado el hito M<n>. Revisa el repo completo contra CLAUDE.md y docs/architecture.md:
- ¿Se respetan las reglas no negociables? (autoridad, un hilo, dominio puro, data-driven, protocolo, vectores)
- Deuda técnica: archivos > 400 líneas, duplicación, tests lentos o frágiles, TODOs.
- ¿Documentación desactualizada?
Dame una lista priorizada y propone HUs técnicas (prefijo HU-T) para lo importante.
```

## Preparar un playtest con amigos
```
Mañana jugamos 4 personas. Prepara:
1. Checklist de despliegue (skill dotnet-server + docs/deploy.md) y verifica /health.
2. 4 cuentas de prueba o instrucciones de registro para ellos.
3. Un mensaje corto para enviarles (link web, controles básicos, cómo formar grupo).
4. Qué métricas mirar durante la sesión y dónde.
5. Cómo cronometrar a un jugador nuevo desde que abre el enlace hasta su primera pelea en grupo (objetivo ≤ 5 min, pilar 1).
```

## Convertir feedback en HUs
```
Estas son las notas del playtest: <pega notas>.
Contrasta cada petición de diseño con los pilares de docs/design/gdd.md y marca como candidata a descartar la que no
sirva a ninguno (dime por qué).
Agrúpalas por tema, separa bugs de peticiones de diseño, y crea las HUs correspondientes en docs/backlog/
con la plantilla (criterios Dado/Cuando/Entonces), prioridad y estimación. Actualiza el índice de README.md.
```
