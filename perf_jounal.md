## This tests were ran outside any container and withou lb

| Change description | p99 | Local Score |
| --- | --- | --- |
| Initial version of brute-force scan | 1257.69ms | 2719.81 |
| Using padded vectors to 16 positions | 735.14ms | 2953.01 |
| Unrolled loops with the distance calculation| 626.92ms | 3022.17 |