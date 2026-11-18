"""Local TCP echo control only; NOT a robot, SDK or route/NAT test."""
import asyncio, json
PORTS=(20003,20004,8080)
async def handle(r,w):
    try:
        while d:=await r.read(4096):
            w.write(d); await w.drain()
    finally:
        w.close(); await w.wait_closed()
async def client(p,n):
    data=(f'port={p},round={n};'.encode()*10000)
    r,w=await asyncio.open_connection('127.0.0.1',p)
    w.write(data); await w.drain(); w.write_eof()
    got=await r.read(); w.close(); await w.wait_closed()
    assert got==data,(p,n,len(got))
    return len(got)
async def main():
    servers=[]
    try:
        for p in PORTS:
            servers.append(await asyncio.start_server(handle,'127.0.0.1',p))
        results=[]
        for n in range(25): results.extend(await asyncio.wait_for(asyncio.gather(*(client(p,n) for p in PORTS)), timeout=15))
        print(json.dumps({'scope':'loopback echo transport control, NOT SDK, NAT or robot validation','ports':PORTS,'rounds':25,'sessions':len(results),'verified_bytes':sum(results),'result':'PASS'}))
    finally:
        for s in servers:s.close()
        await asyncio.gather(*(s.wait_closed() for s in servers))
if __name__ == '__main__':
    asyncio.run(main())
