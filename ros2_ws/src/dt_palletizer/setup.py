from glob import glob
from setuptools import setup

package_name = 'dt_palletizer'

setup(
    name=package_name,
    version='0.0.1',
    packages=[package_name],
    data_files=[
        ('share/ament_index/resource_index/packages', ['resource/' + package_name]),
        ('share/' + package_name, ['package.xml']),
        ('share/' + package_name + '/config', glob('config/*.yaml')),
    ],
    install_requires=['setuptools'],
    zip_safe=True,
    maintainer='INDEX',
    maintainer_email='index-team@example.com',
    description='출고 팔레트 적재·정리 (palletizer_node)',
    license='Apache-2.0',
    tests_require=['pytest'],
    entry_points={
        'console_scripts': [
            'palletizer_node = dt_palletizer.palletizer_node:main',
        ],
    },
)
