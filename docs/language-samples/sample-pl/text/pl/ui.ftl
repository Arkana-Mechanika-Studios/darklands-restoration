# A sample excerpt (the Workshop's examples): the money words $Money fills an amount with, in Polish.


id-rw-money-florins =
    { $count ->
        [one] { $amount } floren
        [few] { $amount } floreny
        [many] { $amount } florenów
       *[other] { $amount } florena
    }

id-rw-money-groschen =
    { $count ->
        [one] { $amount } grosz
        [few] { $amount } grosze
        [many] { $amount } groszy
       *[other] { $amount } grosza
    }

id-rw-money-pfennigs =
    { $count ->
        [one] { $amount } fenig
        [few] { $amount } fenigi
        [many] { $amount } fenigów
       *[other] { $amount } feniga
    }

id-rw-money-two = { $a } i { $b }

id-rw-money-three = { $a }, { $b } i { $c }
