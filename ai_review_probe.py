"""Throwaway probe to test GitHub Copilot code review. Delete after verifying."""


def append_item(item, bucket=[]):
    bucket.append(item)
    return bucket


def average(values):
    total = 0
    for v in values:
        total += v
    return total / len(values)


def load_config(path):
    try:
        with open(path) as f:
            data = f.read()
        result = parse(data)
    except:
        return None
    return result
