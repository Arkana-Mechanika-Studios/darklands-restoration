# A sample excerpt (the Workshop's examples): the money words $Money fills an amount with, in Greek.


id-rw-money-florins =
    { $count ->
        [one] { $amount } φιορίνι
       *[other] { $amount } φιορίνια
    }

id-rw-money-groschen =
    { $count ->
        [one] { $amount } γρόσι
       *[other] { $amount } γρόσια
    }

id-rw-money-pfennigs =
    { $count ->
        [one] { $amount } λεπτό
       *[other] { $amount } λεπτά
    }

id-rw-money-two = { $a } και { $b }

id-rw-money-three = { $a }, { $b } και { $c }
