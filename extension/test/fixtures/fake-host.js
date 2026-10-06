// HostClient 단위 테스트용 가짜 호스트(줄 단위 JSON, 실제 호스트와 같은 규약).
// 요청의 method로 동작을 고른다:
//   ping            → 즉시 ok {pid}
//   delay           → params.ms 후 ok {tag: params.tag}
//   hang            → 응답하지 않음(타임아웃 시험)
//   crash           → 응답 없이 종료 코드 3
//   fail            → ok:false {code:'XamlParse', message, line:2, col:5}
//   garbage         → JSON이 아닌 줄 + 짝 없는 id 응답을 먼저 보낸 뒤 정상 응답
//   split           → 응답 한 줄을 두 조각으로 나눠 보냄
//   render          → params.ms 후 ok {png, width, height, elements, warnings, seq(호스트가 받은 render 순번)}
//   shutdown        → ok 후 종료 코드 0
const readline = require('readline');

let renderSeq = 0;
const send = (obj) => process.stdout.write(JSON.stringify(obj) + '\n');
const rl = readline.createInterface({ input: process.stdin });

rl.on('line', (line) => {
    const req = JSON.parse(line);
    const { id, method, params } = req;
    switch (method) {
        case 'ping':
            send({ id, ok: true, result: { pid: process.pid } });
            break;
        case 'delay':
            setTimeout(() => send({ id, ok: true, result: { tag: params.tag } }), params.ms);
            break;
        case 'hang':
            break;
        case 'crash':
            process.exit(3);
            break;
        case 'fail':
            send({ id, ok: false, error: { code: 'XamlParse', message: 'bad xaml', line: 2, col: 5 } });
            break;
        case 'garbage':
            process.stdout.write('this is not json\n');
            send({ id: 99999, ok: true, result: {} });
            send({ id, ok: true, result: { survived: true } });
            break;
        case 'split': {
            const text = JSON.stringify({ id, ok: true, result: { text: 'split-ok' } }) + '\n';
            const mid = Math.floor(text.length / 2);
            process.stdout.write(text.slice(0, mid));
            setTimeout(() => process.stdout.write(text.slice(mid)), 50);
            break;
        }
        case 'render': {
            const seq = ++renderSeq;
            setTimeout(
                () => send({ id, ok: true, result: { png: 'AAAA', width: 1, height: 1, elements: [], warnings: [], seq } }),
                (params && params.ms) || 0);
            break;
        }
        case 'shutdown':
            send({ id, ok: true, result: {} });
            setTimeout(() => process.exit(0), 10);
            break;
        default:
            send({ id, ok: false, error: { code: 'UnknownMethod', message: method } });
    }
});

rl.on('close', () => process.exit(0));
