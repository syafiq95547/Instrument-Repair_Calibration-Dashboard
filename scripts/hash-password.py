#!/usr/bin/env python3
"""Generate Admin__PasswordHash without putting the password in shell history."""
import base64, getpass, hashlib, secrets
password = getpass.getpass("Administrator password: ")
if len(password) < 12:
    raise SystemExit("Use at least 12 characters.")
salt = secrets.token_bytes(16)
iterations = 210000
hash_value = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, iterations, 32)
print(f"{iterations}:{base64.b64encode(salt).decode()}:{base64.b64encode(hash_value).decode()}")
