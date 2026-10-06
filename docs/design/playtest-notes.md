# Notas de la partida de prueba (Fase 1)

Plantilla para la sesión con amigos que cierra HU-084 CA3/CA4, HU-083 CA3 y HU-089 CA3. Se rellena **durante** la partida
(alguien cronometra y apunta) y, al terminar, cada cosa que pida código se convierte en una HU en `docs/backlog/` y se enlaza
aquí. Los números de diseño no se tocan antes de jugar: se comparan con lo que sale.

## Sesión

| Campo | Valor |
|---|---|
| Fecha | 2026-10-05/06 (primera sesión: Fase 1 completa, resultado OK; sin tiempos apuntados) |
| Versión (commit de `main`) | `cdd1688` (PR #18) |
| Servidor | producción (`/play`) o local |
| Jugadores (nombre · clase · navegador o escritorio) | |
| Quién cronometra | |

## Preparación

- Cuentas de administrador para quien cronometra (comandos de chat, HU-070): `/level n`, `/tp x y`, `/tpto Nombre`,
  `/give itemId [qty] [Nombre]`, `/gold n`, `/spawn monsterId [n]`, `/heal`, `/god`.
- Mina: el portal está en la pradera, en `/tp 243 53.5` (nivel mínimo 4). Las palancas de la Sala 2 flanquean la puerta del pasillo
  del jefe, al sur de la sala: (51.5, 32.5) y (62.5, 32.5); con las dos se abre, y dentro hay una tercera que la abre sola.
- Equipo verde de referencia por clase: el de `balance-report.md` §3b (se puede dar con `/give`).

## 1. Primer contacto (HU-084 CA3)

Objetivo: **≤ 5 min** desde abrir el enlace hasta la primera pelea en grupo con un amigo, para alguien que no ha jugado.

| Jugador nuevo | Abre el enlace | Cuenta creada | Personaje creado | Entra al mundo | Primera pelea en grupo | Total | Dónde se atascó |
|---|---|---|---|---|---|---|---|
| | | | | | | | |

## 2. Jefe: Capataz Grask (HU-083 CA3, HU-084 CA4)

Objetivo: **60–100 s** y lo ganan con un sanador o con pociones (1 400 de vida). El modelo da 81–86 s con 3 de nivel 4 y sanador,
~54 s sin sanador y ~101 s con Guerrero + Sacerdote de nivel 6.

| Grupo (clases y niveles) | ¿Sanador? | Duración | ¿Ganaron? | Pociones usadas | Maná al final (Mago / Sacerdote) | Muertes | Notas |
|---|---|---|---|---|---|---|---|
| | | | | | | | |

- ¿Se entendió el puzle de palancas sin explicarlo? ¿Alguien se quedó encerrado o perdido?

## 3. Duelos (HU-084 CA4, HU-101)

Objetivo: con nivel y equipo iguales, el favorito gana el **60–75 %** (simulado: 63–68 %, `rules.classAdvantage`).

| Pareja (favorito primero) | Duelos | Gana el favorito | Duración media | Notas |
|---|---|---|---|---|
| Mago > Guerrero | | | | |
| Guerrero > Pícaro | | | | |
| Pícaro > Mago | | | | |
| Sacerdote > Guerrero | | | | |
| Pícaro > Sacerdote | | | | |
| Sacerdote = Mago | | | | |

- Zona del duelo: ¿se ve bien la línea? ¿El aviso de 5 s se entiende? ¿Alguien perdió por la zona sin querer?
- Sacerdote contra Sacerdote: ¿termina? (en la simulación, el 86 % de esos duelos no acaba).

## 4. Pendientes del modelo (HU-084 CA4)

| Qué | Objetivo | Medido | ¿Abre HU? |
|---|---|---|---|
| Mago solo con básicos contra el Kóbold minero: vida perdida | ≤ 50 % (el modelo da 65 %) | | |
| Ciclo real por monstruo (pelea + descanso) | ~36 s (`killCycleSecTarget`) | | |
| Cruzar una zona de la pradera a pie | 25–40 s (`zoneCrossTimeSecTarget`) | | |

## 5. Rendimiento y navegadores (HU-089 CA3, HU-074 CA1)

- En el equipo de referencia, en el navegador: escribir `/fxbench` en el chat (24 marcas de área y 40 números durante 20 s).
  Objetivo: **FPS p5 ≥ 45**.

| Equipo | Navegador | FPS p5 | ¿OK? |
|---|---|---|---|
| | Chrome | | |
| | Firefox | | |

## 6. Sensaciones

Lo que gustó, lo que molestó, lo que nadie entendió. Una línea por cosa, con quién lo dijo.

-

## HUs creadas a partir de esta sesión

| HU | Título | De qué apartado sale |
|---|---|---|
| | | |
