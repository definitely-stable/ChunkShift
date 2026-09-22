use fastcdc::v2016::FastCDC;

const EXPECTED: &[(usize, usize)] = &[
    (0, 100081),
    (100081, 33106),
    (133187, 69903),
    (203090, 49442),
    (252532, 103705),
    (356237, 144977),
    (501214, 214287),
    (715501, 145480),
    (860981, 50981),
    (911962, 37677),
    (949639, 79457),
    (1029096, 19480),
];

fn xorshift_bytes(length: usize, seed: u32) -> Vec<u8> {
    let mut state = seed;
    let mut bytes = vec![0u8; length];

    for value in &mut bytes {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        *value = state as u8;
    }

    bytes
}

fn main() {
    let input = xorshift_bytes(1024 * 1024, 0x12345678);
    let actual: Vec<(usize, usize)> = FastCDC::new(
        &input,
        16 * 1024,
        64 * 1024,
        256 * 1024,
    )
    .map(|chunk| (chunk.offset, chunk.length))
    .collect();

    assert_eq!(EXPECTED, actual.as_slice());
    println!("fastcdc-rs 5.0.0 v2016 external vector OK: {} chunks", actual.len());
}
