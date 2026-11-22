# A sample excerpt (the Workshop's examples): the money words $Money fills an amount with, in Russian.


id-rw-money-florins =
    { $count ->
        [one] { $amount } флорин
        [few] { $amount } флорина
        [many] { $amount } флоринов
       *[other] { $amount } флорина
    }

id-rw-money-groschen =
    { $count ->
        [one] { $amount } грош
        [few] { $amount } гроша
        [many] { $amount } грошей
       *[other] { $amount } гроша
    }

id-rw-money-pfennigs =
    { $count ->
        [one] { $amount } пфенниг
        [few] { $amount } пфеннига
        [many] { $amount } пфеннигов
       *[other] { $amount } пфеннига
    }

id-rw-money-two = { $a } и { $b }

id-rw-money-three = { $a }, { $b } и { $c }
