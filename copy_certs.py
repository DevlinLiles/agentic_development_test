import shutil
import os

src = '/usr/local/share/ca-certificates/orbstack-root.crt'
for dst in ['server/orbstack-root.crt', 'client/orbstack-root.crt']:
    shutil.copyfile(src, dst)
    print('copied', os.path.getsize(dst), 'bytes to', dst)
