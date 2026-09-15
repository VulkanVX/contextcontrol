"""ContextControl's loopback-only CPU Transformers bridge. No remote Python code is trusted."""
import argparse
import json
import queue
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import torch
from transformers import AutoModelForCausalLM, AutoTokenizer, StoppingCriteria, StoppingCriteriaList, TextIteratorStreamer


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', required=True)
    parser.add_argument('--port', type=int, required=True)
    parser.add_argument('--context', type=int, default=4096)
    parser.add_argument('--threads', type=int, default=0)
    args = parser.parse_args()
    torch.set_num_threads(max(1, args.threads) if args.threads > 0 else min(6, torch.get_num_threads()))
    tokenizer = AutoTokenizer.from_pretrained(args.model, trust_remote_code=False)
    model = AutoModelForCausalLM.from_pretrained(args.model, trust_remote_code=False, use_safetensors=True, dtype=torch.float32)
    model.eval()
    lock = threading.Lock()

    class Stop(StoppingCriteria):
        def __init__(self, event): self.event = event
        def __call__(self, input_ids, scores, **kwargs): return self.event.is_set()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = 'HTTP/1.0'
        def log_message(self, *items): pass  # never log prompts or authorization headers
        def result(self, status, body):
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            self.wfile.write(json.dumps(body).encode())

        def do_GET(self):
            if self.path != '/v1/models': return self.result(404, {'error': 'Not found'})
            self.result(200, {'object': 'list', 'data': [{'id': args.model, 'object': 'model'}]})

        def do_POST(self):
            if self.path != '/v1/chat/completions': return self.result(404, {'error': 'Not found'})
            if self.headers.get('Content-Type', '').split(';')[0] != 'application/json':
                return self.result(415, {'error': 'Use application/json'})
            if not lock.acquire(blocking=False): return self.result(409, {'error': 'A model response is already running'})
            stop = threading.Event()
            worker = None
            try:
                length = int(self.headers.get('Content-Length', '0'))
                if not 0 < length <= 2_000_000: return self.result(413, {'error': 'Invalid or oversized request'})
                request = json.loads(self.rfile.read(length))
                messages = request.get('messages', [])
                if not messages or any(not isinstance(m.get('content'), str) for m in messages):
                    return self.result(400, {'error': 'This Transformers runtime supports text messages only'})
                if request.get('model') != args.model: return self.result(404, {'error': 'This model is not loaded'})
                kwargs = request.get('chat_template_kwargs') or {}
                text = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True,
                                                     enable_thinking=kwargs.get('enable_thinking', False))
                inputs = tokenizer(text, return_tensors='pt', add_special_tokens=False)
                prompt_tokens = inputs.input_ids.shape[-1]
                context = min(args.context, getattr(model.config, 'max_position_embeddings', args.context))
                output_limit = max(1, min(int(request.get('max_tokens', 512)), context - prompt_tokens))
                if prompt_tokens >= context: return self.result(400, {'error': 'Prompt exceeds model context; shorten the chat or raise context'})
                streamer = TextIteratorStreamer(tokenizer, skip_prompt=True, skip_special_tokens=True, timeout=1)
                outcome = {}
                def generate():
                    try:
                        with torch.inference_mode():
                            tokens = model.generate(**inputs, max_new_tokens=output_limit, do_sample=False, streamer=streamer,
                                                    stopping_criteria=StoppingCriteriaList([Stop(stop)]), pad_token_id=tokenizer.eos_token_id)
                        outcome['tokens'] = int(tokens.shape[-1] - prompt_tokens)
                    except Exception as ex:
                        outcome['error'] = str(ex)
                        streamer.end()
                self.send_response(200)
                self.send_header('Content-Type', 'text/event-stream')
                self.send_header('Cache-Control', 'no-cache')
                self.end_headers()
                def event(body):
                    data = body if isinstance(body, str) else json.dumps(body)
                    self.wfile.write(('data: ' + data + '\n\n').encode())
                    self.wfile.flush()
                worker = threading.Thread(target=generate, daemon=True)
                worker.start()
                iterator = iter(streamer)
                while True:
                    try:
                        chunk = next(iterator)
                    except StopIteration:
                        break
                    except queue.Empty:
                        self.wfile.write(b': keep-alive\n\n')
                        self.wfile.flush()  # detect client cancellation even while the model is preparing
                        continue
                    event({'choices': [{'index': 0, 'delta': {'content': chunk}, 'finish_reason': None}]})
                worker.join()
                if 'error' in outcome: event({'error': {'message': outcome['error']}})
                else:
                    count = outcome.get('tokens', 0)
                    event({'choices': [{'index': 0, 'delta': {}, 'finish_reason': 'length' if count >= output_limit else 'stop'}],
                           'usage': {'prompt_tokens': prompt_tokens, 'completion_tokens': count}})
                    event('[DONE]')
            except (BrokenPipeError, ConnectionResetError): pass
            except Exception as ex:
                # Errors after streaming started are reported in the SSE protocol, never as an empty success.
                try:
                    if worker is not None: event({'error': {'message': str(ex)}})
                    else: self.result(400, {'error': str(ex)})
                except OSError: pass
            finally:
                stop.set()
                if worker is not None: worker.join()  # hold the lock until generation really stops
                lock.release()

    print('ContextControl Transformers model ready', flush=True)
    ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()


if __name__ == '__main__': main()
