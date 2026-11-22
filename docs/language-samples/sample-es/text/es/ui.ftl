# A sample excerpt (the Workshop's examples): the money words $Money fills an amount with, in Spanish.


id-rw-money-florins =
    { $count ->
        [one] { $amount } florín
       *[other] { $amount } florines
    }

id-rw-money-groschen =
    { $count ->
        [one] { $amount } grueso
       *[other] { $amount } gruesos
    }

id-rw-money-pfennigs =
    { $count ->
        [one] { $amount } dinero
       *[other] { $amount } dineros
    }

id-rw-money-two = { $a } y { $b }

id-rw-money-three = { $a }, { $b } y { $c }
