## This tests were ran outside any container and withou lb

| Change description | p99 | Local Score | Version Tag |
| --- | --- | --- | ---|
| Initial version of brute-force scan | 1257.69ms | 2719.81 | |
| Using padded vectors to 16 positions | 735.14ms | 2953.01 | |
| Unrolled loops with the distance calculation| 626.92ms | 3022.17 | V0.1 |
| Changed response submission to supress a header and explicitly write the data | 587.29ms | 3050.53 | V0.2